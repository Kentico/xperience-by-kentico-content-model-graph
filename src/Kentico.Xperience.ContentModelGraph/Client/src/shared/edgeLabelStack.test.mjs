// Run with `npm test` (Node's own test runner strips the types off the module under test).
//
// Two edges between the same pair of nodes put their chips in the same place: two fields pointing the same
// way are one curve, and two nodes referencing each other are two mirror-image curves crossing at their
// shared centre. These cover which edges are stacked, in what order, and how far from the anchor each chip
// lands - an edge alone between its nodes must stay exactly where it always was.

import test from "node:test";
import assert from "node:assert/strict";

import {
  EDGE_LABEL_STACK_GAP as GAP,
  edgeLabelStackAnchor,
  edgeLabelTransform,
  stackEdgeLabels,
} from "./edgeLabelStack.ts";

const edge = (id, source, target, height = 22) => ({
  id,
  source,
  target,
  height,
});

test("an edge alone between its nodes gets no slot", () => {
  const slots = stackEdgeLabels([edge("ab", "a", "b"), edge("bc", "b", "c")]);

  assert.equal(slots.size, 0);
});

test("reciprocal edges are stacked either side of the anchor", () => {
  const slots = stackEdgeLabels([
    edge("ba", "b", "a", 38),
    edge("ab", "a", "b", 22),
  ]);

  // The edge running from the lower id to the higher comes first, whatever order the input is in.
  assert.deepEqual(slots.get("ab"), { align: "above", offset: GAP / 2 });
  assert.deepEqual(slots.get("ba"), { align: "below", offset: GAP / 2 });
});

test("parallel edges in the same direction are stacked in input order", () => {
  const slots = stackEdgeLabels([
    edge("first", "a", "b"),
    edge("second", "a", "b"),
  ]);

  assert.deepEqual(slots.get("first"), { align: "above", offset: GAP / 2 });
  assert.deepEqual(slots.get("second"), { align: "below", offset: GAP / 2 });
});

test("an odd stack centres its middle chip and clears it on both sides", () => {
  const slots = stackEdgeLabels([
    edge("ab1", "a", "b", 22),
    edge("ab2", "a", "b", 38),
    edge("ba", "b", "a", 22),
  ]);

  assert.deepEqual(slots.get("ab1"), { align: "above", offset: 38 / 2 + GAP });
  assert.deepEqual(slots.get("ab2"), { align: "centre", offset: 0 });
  assert.deepEqual(slots.get("ba"), { align: "below", offset: 38 / 2 + GAP });
});

test("a chip clears every chip between it and the anchor", () => {
  const slots = stackEdgeLabels([
    edge("1", "a", "b", 10),
    edge("2", "a", "b", 20),
    edge("3", "a", "b", 30),
    edge("4", "a", "b", 40),
  ]);

  assert.deepEqual(slots.get("1"), {
    align: "above",
    offset: GAP / 2 + 20 + GAP,
  });
  assert.deepEqual(slots.get("2"), { align: "above", offset: GAP / 2 });
  assert.deepEqual(slots.get("3"), { align: "below", offset: GAP / 2 });
  assert.deepEqual(slots.get("4"), {
    align: "below",
    offset: GAP / 2 + 30 + GAP,
  });
});

test("an edge with no chip takes no place in the stack", () => {
  // Hiding field names empties every label, so nothing is stacked and every edge stays centred.
  assert.equal(
    stackEdgeLabels([edge("ab", "a", "b", 0), edge("ba", "b", "a", 0)]).size,
    0,
  );

  // One labelled edge left in the pair is alone again.
  assert.equal(
    stackEdgeLabels([edge("ab", "a", "b"), edge("ba", "b", "a", 0)]).size,
    0,
  );
});

test("pairs are grouped independently", () => {
  const slots = stackEdgeLabels([
    edge("ab", "a", "b"),
    edge("ba", "b", "a"),
    edge("ac", "a", "c"),
    edge("ca", "c", "a"),
  ]);

  assert.equal(slots.get("ab")?.align, "above");
  assert.equal(slots.get("ba")?.align, "below");
  assert.equal(slots.get("ac")?.align, "above");
  assert.equal(slots.get("ca")?.align, "below");
});

test("the anchor is halfway between the node centres", () => {
  const anchor = edgeLabelStackAnchor(
    { x: 0, y: 0, width: 200, height: 80 },
    { x: 400, y: 300, width: 200, height: 100 },
  );

  assert.deepEqual(anchor, { x: 300, y: 195 });
  assert.equal(
    edgeLabelStackAnchor(undefined, { x: 0, y: 0, width: 1, height: 1 }),
    undefined,
  );
});

test("an unslotted chip is centred on its point, as it always was", () => {
  assert.equal(
    edgeLabelTransform(10, 20),
    "translate(-50%, -50%) translate(10px, 20px)",
  );
  assert.equal(
    edgeLabelTransform(10, 20, { align: "centre", offset: 0 }),
    "translate(-50%, -50%) translate(10px, 20px)",
  );
});

test("a stacked chip hangs above or stands below the anchor", () => {
  assert.equal(
    edgeLabelTransform(10, 20, { align: "above", offset: 2 }),
    "translate(-50%, -100%) translate(10px, 18px)",
  );
  assert.equal(
    edgeLabelTransform(10, 20, { align: "below", offset: 2 }),
    "translate(-50%, 0%) translate(10px, 22px)",
  );
});
