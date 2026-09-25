// The canvas state the relationships graph accumulates, and the derivations the canvas is drawn from.
// Everything here is pure: the component owns the React state and the rendering, this owns what the state
// means. Keeping it apart is what makes the derivations testable without a canvas - a dangling edge or a
// duplicated edge id is a property of these maps, not of anything ReactFlow does with them.

import type { RelationshipExpandDirection } from "./RelationshipNode";
import type {
  ContentItemRelationshipDto,
  ContentItemRelationshipGraphDto,
  ContentItemRelationshipItemDto,
} from "./model";

export interface RelationshipEdgeRecord {
  // The server's relationship id. It travels with the record so an export can quote it, but it is *not*
  // unique across the graph: the server builds it from one endpoint and the reference group, so the same
  // id comes back for two different references once the graph reaches past the item it was rooted at - a
  // taxonomy id (`outgoing:taxonomy:<field>:<tag>`) names no source item at all, and an incoming id names
  // no target. Nothing that has to identify an edge may be keyed by it.
  readonly id: string;
  readonly source: string;
  readonly target: string;
  readonly label: string;
  // The field the reference was found on, independent of how the edge is labelled. Layout groups the
  // nodes reached through one field together, so it needs the field itself rather than its label.
  readonly field: string;
  readonly path?: string | null;
  readonly origin: string;
}

// One drawn edge: every record between the same ordered pair of nodes, merged.
export interface RelationshipMergedEdge {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  readonly field: string;
  readonly records: readonly RelationshipEdgeRecord[];
}

export const itemNodeId = (item: ContentItemRelationshipItemDto) =>
  item.identifier ?? item.itemId?.toString() ?? item.codeName;

export const expandKey = (
  nodeId: string,
  direction: RelationshipExpandDirection,
) => `${nodeId}:${direction}`;

// The field a relationship was found on. The code name is the stable one - a Page Builder property keeps
// the same code name across personalization variants while its label spells the variants out - so it wins
// wherever one is available, and the label is the fallback for sources that carry no code name.
export const relationshipField = (relationship: ContentItemRelationshipDto) =>
  relationship.fieldCodeName || relationship.fieldLabel;

// Same reference can be discovered from either endpoint's fetch with a different relationship id,
// so edges are deduplicated by their actual endpoints and field rather than that id.
export const edgeContentKey = (source: string, target: string, field: string) =>
  `${source}=>${target}:${field}`;

// What a drawn edge is identified by. The endpoints are the only thing about an edge that is unique on the
// canvas - one curve per ordered pair - so they, and not any server id, are what ReactFlow keys the edge on
// and what React keys its element on.
export const mergedEdgeId = (source: string, target: string) =>
  `${source}=>${target}`;

export const initialItems = (graph: ContentItemRelationshipGraphDto) => {
  const items = new Map<string, ContentItemRelationshipItemDto>();
  items.set(itemNodeId(graph.rootItem), graph.rootItem);

  for (const relationship of [
    ...(graph.incoming ?? []),
    ...(graph.outgoing ?? []),
  ]) {
    items.set(itemNodeId(relationship.relatedItem), relationship.relatedItem);
  }

  return items;
};

export const initialEdges = (graph: ContentItemRelationshipGraphDto) => {
  const rootId = itemNodeId(graph.rootItem);
  const edges = new Map<string, RelationshipEdgeRecord>();

  for (const relationship of graph.incoming ?? []) {
    const source = itemNodeId(relationship.relatedItem);
    const field = relationshipField(relationship);
    edges.set(edgeContentKey(source, rootId, field), {
      id: relationship.id,
      source,
      target: rootId,
      label: relationship.fieldLabel || relationship.fieldCodeName,
      field,
      path: relationship.fieldPath,
      origin: expandKey(rootId, "incoming"),
    });
  }

  for (const relationship of graph.outgoing ?? []) {
    const target = itemNodeId(relationship.relatedItem);
    const field = relationshipField(relationship);
    edges.set(edgeContentKey(rootId, target, field), {
      id: relationship.id,
      source: rootId,
      target,
      label: relationship.fieldLabel || relationship.fieldCodeName,
      field,
      path: relationship.fieldPath,
      origin: expandKey(rootId, "outgoing"),
    });
  }

  return edges;
};

// The item was deleted after the graph was rendered. Flagging it keeps the metadata and the edges already
// discovered for it while stopping it offering expansion. Returns the map unchanged when it already says
// so, because replacing state with an equal value only costs a render.
export const markItemMissing = (
  items: ReadonlyMap<string, ContentItemRelationshipItemDto>,
  nodeId: string,
) => {
  const existing = items.get(nodeId);

  if (!existing || existing.isMissing) {
    return items;
  }

  return new Map(items).set(nodeId, { ...existing, isMissing: true });
};

export const mergeExpandedItems = (
  items: ReadonlyMap<string, ContentItemRelationshipItemDto>,
  relationships: readonly ContentItemRelationshipDto[],
) => {
  const next = new Map(items);

  for (const relationship of relationships) {
    next.set(itemNodeId(relationship.relatedItem), relationship.relatedItem);
  }

  return next;
};

// An expansion replaces everything it discovered last time rather than adding to it, so a reference removed
// since is dropped: every record this expansion is the origin of goes before the response's own are added.
export const mergeExpandedEdges = (
  edgeRecords: ReadonlyMap<string, RelationshipEdgeRecord>,
  nodeId: string,
  direction: RelationshipExpandDirection,
  relationships: readonly ContentItemRelationshipDto[],
) => {
  const key = expandKey(nodeId, direction);
  const next = new Map(edgeRecords);

  for (const [contentKey, record] of edgeRecords) {
    if (record.origin === key) {
      next.delete(contentKey);
    }
  }

  for (const relationship of relationships) {
    const relatedId = itemNodeId(relationship.relatedItem);
    const source = direction === "incoming" ? relatedId : nodeId;
    const target = direction === "incoming" ? nodeId : relatedId;
    const field = relationshipField(relationship);

    next.set(edgeContentKey(source, target, field), {
      id: relationship.id,
      source,
      target,
      label: relationship.fieldLabel || relationship.fieldCodeName,
      field,
      path: relationship.fieldPath,
      origin: key,
    });
  }

  return next;
};

// What the canvas draws, derived in one pass so the nodes and the edges cannot disagree about it. `items`
// is append-only, so rendering every entry would leave orphan nodes behind once an expansion drops an
// origin's old edges; an edge whose endpoint is no longer an item would likewise be drawn hanging off
// nothing. The two are the same mistake from either end, so one rule settles both: an edge is drawn when
// both of its endpoints are items still held, and a node is drawn when a drawn edge reaches it. The root
// is always drawn - it is the item the graph was opened for, whether or not anything references it.
export const visibleRelationshipGraph = (
  items: ReadonlyMap<string, ContentItemRelationshipItemDto>,
  edgeRecords: ReadonlyMap<string, RelationshipEdgeRecord>,
  rootId: string,
) => {
  const visibleItems = new Map<string, ContentItemRelationshipItemDto>();
  const visibleRecords: RelationshipEdgeRecord[] = [];
  const rootItem = items.get(rootId);

  if (rootItem) {
    visibleItems.set(rootId, rootItem);
  }

  for (const record of edgeRecords.values()) {
    const source = items.get(record.source);
    const target = items.get(record.target);

    if (!source || !target) {
      continue;
    }

    visibleRecords.push(record);
    visibleItems.set(record.source, source);
    visibleItems.set(record.target, target);
  }

  return { visibleItems, visibleRecords };
};

// Records are keyed by endpoints *and* field, so the same page referencing the same item from its page
// template and from a widget produces two records between the same pair of nodes. Two edges with the same
// endpoints are drawn as the same curve, one exactly on top of the other, and so are their labels, which is
// unreadable and has no repositioning that fixes it - a curve has one sensible place for a label. They are
// merged into one edge whose chip names every reference instead. Nothing is lost: same source, same target,
// every field still stated; only a stroke nobody could see goes away.
//
// Records are sorted by their server id first, so the order of a merged edge's entries - and which record
// speaks for its field - is the same every time the same graph is drawn, whatever order expanding and
// refreshing left the map in.
export const mergeRelationshipEdgeRecords = (
  records: Iterable<RelationshipEdgeRecord>,
): readonly RelationshipMergedEdge[] => {
  const merged = new Map<string, RelationshipEdgeRecord[]>();

  for (const record of [...records].sort((a, b) => a.id.localeCompare(b.id))) {
    const key = mergedEdgeId(record.source, record.target);
    const existing = merged.get(key);

    if (existing) {
      existing.push(record);
    } else {
      merged.set(key, [record]);
    }
  }

  return [...merged.entries()].map(([id, group]) => ({
    id,
    source: group[0].source,
    target: group[0].target,
    // The lowest-id record speaks for the merged edge's field, which is what layout groups the nodes of a
    // rank by.
    field: group[0].field,
    records: group,
  }));
};
