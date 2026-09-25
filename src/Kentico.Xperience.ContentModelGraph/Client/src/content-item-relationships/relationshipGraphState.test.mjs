// Run with `npm test` (Node's own test runner strips the types off the module under test).
//
// These cover what the canvas is derived to be, which is where the graph's two standing hazards live: a
// node left over after the edges that reached it were dropped, and an edge left over after the node it
// reached was dropped. The second one shipped: the canvas kept every edge record whatever became of its
// endpoints, and keyed each drawn edge on the server's relationship id - which is not unique across a graph
// that has been expanded past the item it was rooted at, so React was handed duplicate keys and orphaned
// elements that no later render could reach. Resetting then left edges drawn with no node on either end.

import test from "node:test";
import assert from "node:assert/strict";

import {
  expandKey,
  initialEdges,
  initialItems,
  mergeExpandedEdges,
  mergeExpandedItems,
  mergeRelationshipEdgeRecords,
  visibleRelationshipGraph,
} from "./relationshipGraphState.ts";

// --- fixtures -------------------------------------------------------------

const item = (id, overrides = {}) => ({
  itemId: Number(id.replace(/\D/g, "")) || 1,
  identifier: id,
  displayName: id,
  codeName: id,
  contentTypeDisplayName: "Type",
  contentTypeCodeName: "type",
  kind: "reusable",
  ...overrides,
});

// A taxonomy tag as the server sends it: no `itemId`, and a relationship id built from the field and the
// tag alone - `outgoing:taxonomy:<field>:<tag>` names no source item, so every item tagged with the same
// tag on a same-named field reports the very same id.
const tag = (id) => ({
  itemId: null,
  identifier: id,
  displayName: id,
  codeName: id,
  contentTypeDisplayName: "Taxonomy tag",
  contentTypeCodeName: "taxonomy",
  kind: "taxonomy",
});

const relationship = (id, relatedItem, field) => ({
  id,
  relatedItem,
  fieldLabel: field,
  fieldCodeName: field,
  fieldPath: null,
  direction: "outgoing",
});

const graphOf = (rootItem, { incoming = [], outgoing = [] } = {}) => ({
  rootItem,
  incoming,
  outgoing,
});

// The component's whole derivation, in the order the component performs it.
const canvas = (items, edgeRecords, rootId) => {
  const { visibleItems, visibleRecords } = visibleRelationshipGraph(
    items,
    edgeRecords,
    rootId,
  );
  const edges = mergeRelationshipEdgeRecords(visibleRecords);

  return {
    nodeIds: [...visibleItems.keys()].sort(),
    edgeIds: edges.map((edge) => edge.id).sort(),
    edgePairs: edges.map((edge) => `${edge.source}->${edge.target}`).sort(),
  };
};

const assertSound = (drawn, label) => {
  const nodes = new Set(drawn.nodeIds);

  for (const pair of drawn.edgePairs) {
    const [source, target] = pair.split("->");
    assert.ok(
      nodes.has(source) && nodes.has(target),
      `${label}: edge ${pair} is drawn with no node at one end`,
    );
  }

  assert.equal(
    new Set(drawn.edgeIds).size,
    drawn.edgeIds.length,
    `${label}: two drawn edges share an id (${drawn.edgeIds.join(", ")})`,
  );
};

// --- the reported bug -----------------------------------------------------

// Root and neighbour are both tagged with the same tag, which is the common case: two items of the same
// content type share the tag field's code name, and the server's taxonomy relationship id repeats.
const taxonomyCollisionGraph = () =>
  graphOf(item("root"), {
    outgoing: [
      relationship("outgoing:a-ref", item("a"), "related"),
      relationship("outgoing:taxonomy:tags:TAG-1", tag("TAG-1"), "tags"),
    ],
  });

const expandAOutgoing = () =>
  graphOf(item("a"), {
    outgoing: [
      // Byte for byte the id the root's own tag edge already carries.
      relationship("outgoing:taxonomy:tags:TAG-1", tag("TAG-1"), "tags"),
    ],
  });

test("expanding onto a tag the root already carries draws two distinctly identified edges", () => {
  const graph = taxonomyCollisionGraph();
  const rootId = "root";

  const expandedItems = mergeExpandedItems(
    initialItems(graph),
    expandAOutgoing().outgoing,
  );
  const expandedEdges = mergeExpandedEdges(
    initialEdges(graph),
    "a",
    "outgoing",
    expandAOutgoing().outgoing,
  );

  const drawn = canvas(expandedItems, expandedEdges, rootId);

  assertSound(drawn, "after expanding a");
  // Both references are on the canvas. Keyed on the server id, one of them was swallowed.
  assert.deepEqual(drawn.edgePairs, [
    "a->TAG-1",
    "root->TAG-1",
    "root->a",
  ]);
});

test("resetting after an expansion restores exactly the graph the server first sent", () => {
  const graph = taxonomyCollisionGraph();
  const rootId = "root";
  const pristine = canvas(initialItems(graph), initialEdges(graph), rootId);

  const expandedItems = mergeExpandedItems(
    initialItems(graph),
    expandAOutgoing().outgoing,
  );
  const expandedEdges = mergeExpandedEdges(
    initialEdges(graph),
    "a",
    "outgoing",
    expandAOutgoing().outgoing,
  );
  assertSound(canvas(expandedItems, expandedEdges, rootId), "after expanding");

  // `onReset` rebuilds both maps from the same prop the first render used.
  const reset = canvas(initialItems(graph), initialEdges(graph), rootId);

  assertSound(reset, "after reset");
  assert.deepEqual(reset, pristine);
  assert.deepEqual(reset.edgePairs, ["root->TAG-1", "root->a"]);
});

// --- the two hazards, as properties ---------------------------------------

test("an edge whose endpoint is no longer an item is not drawn", () => {
  const graph = graphOf(item("root"), {
    outgoing: [relationship("outgoing:a-ref", item("a"), "related")],
  });
  const edgeRecords = initialEdges(graph);
  // The item map has lost `a` while the edge to it survives - the shape the reported bug rendered.
  const items = new Map([["root", item("root")]]);

  const drawn = canvas(items, edgeRecords, "root");

  assertSound(drawn, "endpoint missing");
  assert.deepEqual(drawn.nodeIds, ["root"]);
  assert.deepEqual(drawn.edgePairs, []);
});

test("a node no edge reaches any more is not drawn, and one another edge still reaches is", () => {
  const graph = graphOf(item("root"), {
    outgoing: [
      relationship("outgoing:a-ref", item("a"), "related"),
      relationship("outgoing:b-ref", item("b"), "related"),
    ],
  });

  // `a` is refreshed and also references `b`, so `b` hangs off two edges.
  const withA = mergeExpandedEdges(initialEdges(graph), "a", "outgoing", [
    relationship("outgoing:a-b-ref", item("b"), "related"),
  ]);
  let items = mergeExpandedItems(initialItems(graph), [
    relationship("outgoing:a-b-ref", item("b"), "related"),
  ]);

  // The root's outgoing references are refreshed and now name only `a`: the root no longer points at `b`.
  const refreshed = mergeExpandedEdges(withA, "root", "outgoing", [
    relationship("outgoing:a-ref", item("a"), "related"),
  ]);

  const drawn = canvas(items, refreshed, "root");

  assertSound(drawn, "after refresh");
  // `b` stays - `a` still reaches it - but the root's own edge to it is gone.
  assert.deepEqual(drawn.nodeIds, ["a", "b", "root"]);
  assert.deepEqual(drawn.edgePairs, ["a->b", "root->a"]);

  // Drop `a`'s reference too and `b` becomes an orphan, which must not be left behind.
  const emptied = mergeExpandedEdges(refreshed, "a", "outgoing", []);
  const orphaned = canvas(items, emptied, "root");

  assertSound(orphaned, "after emptying");
  assert.deepEqual(orphaned.nodeIds, ["a", "root"]);
  assert.deepEqual(orphaned.edgePairs, ["root->a"]);
});

test("the canvas stays sound across arbitrary expand-and-reset sequences", () => {
  // A ground-truth reference graph, and a server that answers for any item in it. Relationship ids follow
  // the server's own scheme, collisions and all: an incoming id names the source and the reference group
  // but never the target, so two items selected in one field report the same id from either target.
  const rand = ((seed) => () =>
    (seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff)(20250925);

  for (let run = 0; run < 200; run++) {
    const count = 5;
    const fields = ["fa", "fb"];
    const refs = [];

    for (let a = 0; a < count; a++) {
      for (let b = 0; b < count; b++) {
        for (const field of fields) {
          if (a !== b && rand() < 0.2) {
            refs.push({ source: `i${a}`, target: `i${b}`, field });
          }
        }
      }
    }

    const respond = (nodeId) => ({
      rootItem: item(nodeId),
      incoming: refs
        .filter((ref) => ref.target === nodeId)
        .map((ref) => ({
          ...relationship(
            `incoming:${ref.source}:group(${ref.source},${ref.field}):${ref.field}`,
            item(ref.source),
            ref.field,
          ),
          direction: "incoming",
        })),
      outgoing: refs
        .filter((ref) => ref.source === nodeId)
        .map((ref) =>
          relationship(
            `outgoing:${ref.target}:group(${ref.source},${ref.field}):${ref.field}`,
            item(ref.target),
            ref.field,
          ),
        ),
    });

    const rootId = "i0";
    const graph = respond(rootId);
    const pristine = canvas(initialItems(graph), initialEdges(graph), rootId);
    assertSound(pristine, `run ${run} pristine`);

    let items = initialItems(graph);
    let edgeRecords = initialEdges(graph);
    let expanded = new Set([
      expandKey(rootId, "incoming"),
      expandKey(rootId, "outgoing"),
    ]);

    for (let step = 0; step < 6; step++) {
      const { visibleItems } = visibleRelationshipGraph(
        items,
        edgeRecords,
        rootId,
      );
      const ids = [...visibleItems.keys()];
      const nodeId = ids[Math.floor(rand() * ids.length)];
      const direction = rand() < 0.5 ? "incoming" : "outgoing";
      const response = respond(nodeId);
      const relationships =
        direction === "incoming" ? response.incoming : response.outgoing;

      items = mergeExpandedItems(items, relationships);
      edgeRecords = mergeExpandedEdges(
        edgeRecords,
        nodeId,
        direction,
        relationships,
      );
      expanded = new Set(expanded).add(expandKey(nodeId, direction));

      assertSound(
        canvas(items, edgeRecords, rootId),
        `run ${run} step ${step} (${direction} ${nodeId})`,
      );
    }

    const reset = canvas(initialItems(graph), initialEdges(graph), rootId);
    assertSound(reset, `run ${run} reset`);
    assert.deepEqual(reset, pristine, `run ${run}: reset did not restore the graph`);
  }
});
