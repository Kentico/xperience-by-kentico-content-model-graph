import Dagre from "@dagrejs/dagre";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Background,
  Controls,
  MarkerType,
  MiniMap,
  Panel,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type Edge,
} from "@xyflow/react";
import {
  Button,
  ButtonColor,
  ButtonSize,
} from "@kentico/xperience-admin-components";
import { usePageCommandProvider } from "@kentico/xperience-admin-base";

import "@xyflow/react/dist/style.css";
import "./ContentItemRelationships.css";

import {
  estimateRelationshipNodeHeight,
  RELATIONSHIP_NODE_WIDTH,
  RelationshipNodeComponent,
  RelationshipNodeSearchContext,
  type RelationshipExpandDirection,
  type RelationshipExpandStatus,
  type RelationshipNode,
} from "./RelationshipNode";
import {
  estimateRelationshipEdgeLabelSize,
  RelationshipEdgeComponent,
  type RelationshipEdge,
} from "./RelationshipEdge";
import { nodeColor, type NodeKind } from "../content-model-graph/model";
import {
  expandKey,
  initialEdges,
  initialItems,
  itemNodeId,
  markItemMissing,
  mergeExpandedEdges,
  mergeExpandedItems,
  mergeRelationshipEdgeRecords,
  visibleRelationshipGraph,
  type RelationshipEdgeRecord,
} from "./relationshipGraphState";
import type {
  ContentItemRelationshipGraphDto,
  ContentItemRelationshipItemDto,
  ContentItemRelationshipTruncationDto,
} from "./model";

interface ContentItemRelationshipsTemplateProps {
  readonly graph?: ContentItemRelationshipGraphDto | null;
}

interface ExpandRelationshipsArgs {
  readonly itemId: number;
}

const nodeTypes = { relationshipNode: RelationshipNodeComponent };

const edgeTypes = { relationshipEdge: RelationshipEdgeComponent };

const EXPAND_RELATIONSHIPS_COMMAND = "ExpandRelationships";

const REFERENCE_EDGE_COLOR = "#3d5dff";

// Deleted items read as broken references rather than live ones: the edges touching them and their blip on
// the minimap are drawn in this grey instead of the reference colour or the item's kind colour.
const MISSING_COLOR = "#a8a8a8";

// The minimap takes each node's colour from the same helper as the node's left border on the canvas, so the
// two cannot disagree about what kind an item is.
//
// A deleted item is the one exception. Its node is dashed and greyed on the canvas and every edge touching
// it is drawn in `MISSING_COLOR`, so painting it in full kind colour on the map would promise a live item of
// that kind sits there. Grey is still a distinct blip, so nothing becomes harder to find.
//
// Restricted items get no treatment of their own: the canvas draws them like any other node, only without
// links or expansion, and a state the canvas does not show has no business appearing on the map alone. The
// root is left in its kind colour too - the only colour that would read as "root" is the focus blue the node
// is outlined with, which is already the kind colour of every website node, so it would say page, not root.
// The root is also the node the view is fitted around, which is not what a minimap is for finding.
const minimapNodeColor = (node: RelationshipNode) =>
  node.data.item.isMissing
    ? MISSING_COLOR
    : nodeColor(node.data.item.kind as NodeKind);

// ReactFlow's minimap is 200x150 by default, which claims a sizeable corner of a canvas this graph needs.
// 140x105 is the same 4:3 shape at roughly half the area - still large enough to tell the clusters of a
// wide graph apart. The size has to travel through `style` rather than the stylesheet: MiniMap reads
// `style.width`/`style.height` to scale the svg and its viewBox, so sizing it in CSS alone would leave it
// drawing a 200x150 map inside a smaller box.
const MINIMAP_STYLE = { width: 140, height: 105 };

const truncationDirectionLabel = (direction: string) =>
  direction === "incoming" ? "referencing items" : "referenced items";

// The graph is capped server-side, so a capped view must say so - otherwise it reads as a complete one.
const truncationText = (truncation: ContentItemRelationshipTruncationDto) =>
  `Showing ${truncation.shownItemCount} of ${truncation.totalItemCount} ${truncationDirectionLabel(truncation.direction)}.`;

export type RelationshipLayoutDirection = "LR" | "TB";

// Two nodes belong to the same rank when Dagre gave them the same coordinate along the rank axis - `x`
// under `LR`, `y` under `TB`. The values come out of the same per-rank calculation and should be
// identical, but they are floats, so ranks are bucketed with a tolerance rather than compared exactly.
// `ranksep` is 160, so a pixel of slack cannot merge two ranks.
const RANK_TOLERANCE = 1;

// A node's place in the ordering of its rank, as Dagre left it. `position` and `size` are measured along
// the axis the rank runs across - the axis nodes are ordered on within their rank. Under `LR` that is `y`
// and the node's height; under `TB` it is `x` and the node's width.
interface RankMember {
  readonly id: string;
  // Dagre's centre along the position axis, before regrouping.
  readonly position: number;
  readonly size: number;
  readonly groupKey: string;
}

// An edge as seen from one of its endpoints: the node at the other end, and the field it was found on.
interface NodeAnchor {
  readonly edgeId: string;
  readonly anchorId: string;
  readonly field: string;
}

// Which anchor decides a node's group. A node can be touched by several edges - an image referenced by two
// different widgets - so one of them has to win, deterministically, or nodes would swap places between
// renders of the same graph. The anchor nearest the root is the rank the node visually hangs off; the
// lowest edge id settles anything still tied.
const preferredAnchor = (
  anchors: readonly NodeAnchor[],
  rankOf: (nodeId: string) => number,
  rootRank: number,
) =>
  anchors.reduce((best, candidate) => {
    const bestDistance = Math.abs(rankOf(best.anchorId) - rootRank);
    const candidateDistance = Math.abs(rankOf(candidate.anchorId) - rootRank);

    if (candidateDistance !== bestDistance) {
      return candidateDistance < bestDistance ? candidate : best;
    }

    return candidate.edgeId.localeCompare(best.edgeId) < 0 ? candidate : best;
  });

// Reorders one rank so members sharing a group key become contiguous. `byPosition` arrives in Dagre's own
// order, so a Map keyed by group key ends up holding the groups in order of their first - lowest - member,
// which is what keeps Dagre's relative ordering between groups and inside each group. Nothing else is
// allowed to decide the order: a group that moved between renders would make its nodes jump.
const groupRankMembers = (byPosition: readonly RankMember[]) => {
  const groups = new Map<string, RankMember[]>();

  for (const member of byPosition) {
    const existing = groups.get(member.groupKey);

    if (existing) {
      existing.push(member);
    } else {
      groups.set(member.groupKey, [member]);
    }
  }

  return [...groups.values()].flat();
};

// Nodes are not all the same size along the position axis - under `LR` they differ in height - so the new
// order cannot simply take over the old centres: a tall node dropped into a short node's slot would overlap
// its neighbour. The rank is re-flowed instead: it starts at the same leading edge and keeps the sequence of
// gaps Dagre produced between its members. The rank holds the same sizes and the same gaps whatever the
// order, so its total extent, start and end are unchanged, and the space Dagre reserved for edge labels is
// preserved along with them. Returns each node's new leading edge along the position axis.
const reflowRank = (
  byPosition: readonly RankMember[],
  ordered: readonly RankMember[],
) => {
  const gaps = byPosition
    .slice(1)
    .map((member, index) =>
      Math.max(
        0,
        member.position -
          member.size / 2 -
          (byPosition[index].position + byPosition[index].size / 2),
      ),
    );

  const leads = new Map<string, number>();
  let lead = byPosition[0].position - byPosition[0].size / 2;

  ordered.forEach((member, index) => {
    leads.set(member.id, lead);
    lead += member.size + (gaps[index] ?? 0);
  });

  return leads;
};

// `edgeFields` maps an edge id to the field the reference was found on, which is what nodes are grouped by
// within a rank. Edges themselves are untouched: every one keeps its own label, this only changes position.
const layout = (
  nodes: RelationshipNode[],
  edges: RelationshipEdge[],
  edgeFields: ReadonlyMap<string, string>,
  direction: RelationshipLayoutDirection,
) => {
  const graph = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  graph.setGraph({
    rankdir: direction,
    ranksep: 160,
    nodesep: 32,
    edgesep: 24,
  });

  // Ranks run along `x` and nodes are ordered down `y` under `LR`; under `TB` the two axes swap. Every
  // step below that regroups a rank works in those terms rather than in `x`/`y`, so the ordering survives
  // the switch. The one asymmetry is what varies: node heights differ, node widths never do, so a `TB`
  // re-flow is permuting equal-sized slots while an `LR` one is repacking unequal ones.
  const horizontal = direction === "LR";
  const rankCoordOf = (centre: { x: number; y: number }) =>
    horizontal ? centre.x : centre.y;
  const positionCoordOf = (centre: { x: number; y: number }) =>
    horizontal ? centre.y : centre.x;

  const heights = new Map(
    nodes.map((node) => [
      node.id,
      estimateRelationshipNodeHeight(
        node.data.item,
        node.data.canExpand,
        node.data.isRoot,
      ),
    ]),
  );

  nodes.forEach((node) =>
    graph.setNode(node.id, {
      width: RELATIONSHIP_NODE_WIDTH,
      height: heights.get(node.id) ?? 0,
    }),
  );
  // Dagre reserves no space for an edge label unless the edge carries its dimensions, which is why a Page
  // Builder label several times wider than the rank gap ended up drawn across the nodes on either side.
  // This graph is not a multigraph, so two edges sharing a pair of nodes would collapse into one
  // reservation - references between the same pair are merged into a single edge before layout, so there
  // is never more than one edge per pair to reserve for.
  edges.forEach((edge) => {
    const { width, height } = estimateRelationshipEdgeLabelSize(
      edge.data?.entries ?? [],
    );

    graph.setEdge(edge.source, edge.target, { width, height, labelpos: "c" });
  });

  Dagre.layout(graph);

  const centres = new Map(
    nodes.map((node) => {
      const positioned = graph.node(node.id);

      return [node.id, { x: positioned.x, y: positioned.y }];
    }),
  );

  // The size of a node along the position axis, which is what a rank has to pack. Heights are estimated
  // per node; every node is the same width, so a `TB` rank packs uniform slots.
  const positionSizeOf = (nodeId: string) =>
    horizontal ? (heights.get(nodeId) ?? 0) : RELATIONSHIP_NODE_WIDTH;

  // Ranks, as the distinct rank-axis values in ascending order. A node's rank is the index of its bucket,
  // which is also what "nearest the root" is measured in, so incoming and outgoing need no special-casing.
  const rankCoords: number[] = [];
  for (const coord of [...centres.values()]
    .map(rankCoordOf)
    .sort((a, b) => a - b)) {
    if (
      rankCoords.length === 0 ||
      Math.abs(coord - rankCoords[rankCoords.length - 1]) > RANK_TOLERANCE
    ) {
      rankCoords.push(coord);
    }
  }

  const rankOf = (nodeId: string) => {
    const centre = centres.get(nodeId);
    const coord = centre ? rankCoordOf(centre) : 0;
    const rank = rankCoords.findIndex(
      (rankCoord) => Math.abs(coord - rankCoord) <= RANK_TOLERANCE,
    );

    return rank < 0 ? 0 : rank;
  };

  const anchors = new Map<string, NodeAnchor[]>();
  const addAnchor = (nodeId: string, anchor: NodeAnchor) => {
    const existing = anchors.get(nodeId);

    if (existing) {
      existing.push(anchor);
    } else {
      anchors.set(nodeId, [anchor]);
    }
  };

  for (const edge of edges) {
    const field = edgeFields.get(edge.id) ?? edge.id;

    addAnchor(edge.source, {
      edgeId: edge.id,
      anchorId: edge.target,
      field,
    });
    addAnchor(edge.target, {
      edgeId: edge.id,
      anchorId: edge.source,
      field,
    });
  }

  const rootRank = rankOf(nodes.find((node) => node.data.isRoot)?.id ?? "");

  // A node with no edges is its own group, so it stays where Dagre put it relative to everything else.
  const groupKeyOf = (nodeId: string) => {
    const nodeAnchors = anchors.get(nodeId);

    if (!nodeAnchors?.length) {
      return `\n${nodeId}`;
    }

    const anchor = preferredAnchor(nodeAnchors, rankOf, rootRank);

    return `${anchor.anchorId}\n${anchor.field}`;
  };

  const ranks = new Map<number, RankMember[]>();
  for (const node of nodes) {
    const rank = rankOf(node.id);
    const centre = centres.get(node.id);
    const member: RankMember = {
      id: node.id,
      position: centre ? positionCoordOf(centre) : 0,
      size: positionSizeOf(node.id),
      groupKey: groupKeyOf(node.id),
    };

    const existing = ranks.get(rank);

    if (existing) {
      existing.push(member);
    } else {
      ranks.set(rank, [member]);
    }
  }

  const leads = new Map<string, number>();
  for (const members of ranks.values()) {
    // Ties on the position axis are broken by id so a rank's starting order - and therefore its grouping -
    // is the same every time the same graph is laid out. The root is alone in its rank and comes through
    // untouched.
    const byPosition = [...members].sort(
      (a, b) => a.position - b.position || a.id.localeCompare(b.id),
    );

    for (const [id, lead] of reflowRank(
      byPosition,
      groupRankMembers(byPosition),
    )) {
      leads.set(id, lead);
    }
  }

  // ReactFlow positions a node by its top-left corner. The re-flow already produced the leading edge along
  // the position axis; the rank axis keeps Dagre's centre, converted to a leading edge by the node's size
  // on that axis - its width under `LR`, its height under `TB`.
  return nodes.map((node) => {
    const centre = centres.get(node.id) ?? { x: 0, y: 0 };
    const lead = leads.get(node.id) ?? 0;

    return {
      ...node,
      position: horizontal
        ? { x: centre.x - RELATIONSHIP_NODE_WIDTH / 2, y: lead }
        : { x: lead, y: centre.y - (heights.get(node.id) ?? 0) / 2 },
    };
  });
};

const createTimestamp = (date: Date) => {
  const parts = [date.getFullYear(), date.getMonth() + 1, date.getDate()];
  const time = [date.getHours(), date.getMinutes(), date.getSeconds()];

  return [...parts, ...time]
    .map((part) => String(part).padStart(2, "0"))
    .join("");
};

// The download is named after the item the graph was opened from. Display names carry spaces, slashes and
// quotes, none of which belong in a file name, so anything outside the safe set collapses to a hyphen.
const exportFileNameBase = (item: ContentItemRelationshipItemDto) =>
  (item.codeName || item.displayName || "content-item")
    .replace(/[^a-zA-Z0-9._-]+/g, "-")
    .replace(/^-+|-+$/g, "") || "content-item";

// Exports what is on the canvas, not what the server first sent: every item and reference accumulated by
// expanding, so a shared file reproduces the graph the person was actually looking at.
const exportJson = (
  rootItem: ContentItemRelationshipItemDto,
  rootId: string,
  items: ReadonlyMap<string, ContentItemRelationshipItemDto>,
  edgeRecords: readonly RelationshipEdgeRecord[],
  languageCode: string | null,
) => {
  const payload = {
    rootId,
    // The language the graph was viewed in. Each item also carries the language it resolved to, which
    // differs from this one wherever language fallback applied.
    languageCode,
    rootItem,
    items: [...items.values()],
    edges: edgeRecords.map((record) => ({
      id: record.id,
      source: record.source,
      target: record.target,
      label: record.label,
      field: record.field,
      path: record.path ?? null,
    })),
  };

  const blob = new Blob([JSON.stringify(payload, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");

  link.href = url;
  link.download = `${exportFileNameBase(rootItem)}-relationships-${createTimestamp(new Date())}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
};

const ContentItemRelationshipsGraph = ({
  graph,
}: {
  readonly graph: ContentItemRelationshipGraphDto;
}) => {
  const { fitView } = useReactFlow();
  const [search, setSearch] = useState("");
  const [direction, setDirection] = useState<RelationshipLayoutDirection>("LR");
  // Labels are two-line chips that claim layout space of their own, so hiding them is the cheapest way to
  // read a dense graph. They start visible: a reference nobody can name is of little use.
  const [showEdgeLabels, setShowEdgeLabels] = useState(true);
  // The minimap is an occasional orientation aid, not something to read while working, so it starts
  // collapsed to its toggle and gives the canvas the corner back.
  const [minimapExpanded, setMinimapExpanded] = useState(false);
  const rootId = useMemo(() => itemNodeId(graph.rootItem), [graph.rootItem]);

  // Held as read-only maps: every transition replaces them through the helpers in `relationshipGraphState`,
  // and a map mutated in place would not re-render anything that reads it.
  const [items, setItems] = useState<
    ReadonlyMap<string, ContentItemRelationshipItemDto>
  >(() => initialItems(graph));
  const [edgeRecords, setEdgeRecords] = useState<
    ReadonlyMap<string, RelationshipEdgeRecord>
  >(() => initialEdges(graph));
  // The nodes and the edges the canvas draws come out of one derivation, so neither can outlive the other:
  // no node left hanging with no edge, no edge left hanging with no node.
  const { visibleItems, visibleRecords } = useMemo(
    () => visibleRelationshipGraph(items, edgeRecords, rootId),
    [items, edgeRecords, rootId],
  );
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(
    () =>
      new Set([expandKey(rootId, "incoming"), expandKey(rootId, "outgoing")]),
  );
  const [loading, setLoading] = useState<ReadonlySet<string>>(new Set());
  const { executeCommand } = usePageCommandProvider();
  // Bumped by every reset. An expansion already in flight when the graph is reset would otherwise merge
  // its nodes back in afterwards, so a response is only merged while the generation it was asked under is
  // still the current one.
  const generation = useRef(0);

  const mergeExpansion = useCallback(
    (
      nodeId: string,
      direction: RelationshipExpandDirection,
      response: ContentItemRelationshipGraphDto,
    ) => {
      const key = expandKey(nodeId, direction);

      // The item was deleted after the graph was rendered. Flag the node so it renders as missing
      // and stops offering expansion, keeping the metadata and edges already discovered for it.
      if (response.rootItem?.isMissing) {
        setItems((current) => markItemMissing(current, nodeId));
        return;
      }

      const relationships =
        direction === "incoming"
          ? (response.incoming ?? [])
          : (response.outgoing ?? []);

      setItems((current) => mergeExpandedItems(current, relationships));
      setEdgeRecords((current) =>
        mergeExpandedEdges(current, nodeId, direction, relationships),
      );
      setExpanded((current) => new Set(current).add(key));
    },
    [],
  );

  const handleExpand = useCallback(
    async (
      item: ContentItemRelationshipItemDto,
      direction: RelationshipExpandDirection,
    ) => {
      if (!item.itemId) {
        return;
      }

      const nodeId = itemNodeId(item);
      const key = expandKey(nodeId, direction);
      const requestedAt = generation.current;

      setLoading((current) => new Set(current).add(key));

      try {
        // The response is merged against the node and direction captured here, so concurrent
        // expansions of different nodes cannot be matched to the wrong request.
        const response = await executeCommand<
          ContentItemRelationshipGraphDto,
          ExpandRelationshipsArgs
        >(EXPAND_RELATIONSHIPS_COMMAND, { itemId: item.itemId });

        if (response && requestedAt === generation.current) {
          mergeExpansion(nodeId, direction, response);
        }
      } finally {
        setLoading((current) => {
          // A reset already emptied the set, and replacing it with an equal one would re-render for
          // nothing.
          if (!current.has(key)) {
            return current;
          }

          const next = new Set(current);
          next.delete(key);
          return next;
        });
      }
    },
    [executeCommand, mergeExpansion],
  );

  // Everything the canvas accumulates lives in this component, so the reset is local: the one-hop graph the
  // server sent is rebuilt from the same prop the initial render used, and the expansion bookkeeping goes
  // back to the two expansions that graph already represents. View state follows the content model graph's
  // reset and returns to its defaults as well.
  const onReset = useCallback(() => {
    generation.current += 1;

    setItems(initialItems(graph));
    setEdgeRecords(initialEdges(graph));
    setExpanded(
      new Set([expandKey(rootId, "incoming"), expandKey(rootId, "outgoing")]),
    );
    setLoading(new Set());
    setSearch("");
    setDirection("LR");
    setShowEdgeLabels(true);
  }, [graph, rootId]);

  const expandStatus = useCallback(
    (
      nodeId: string,
      direction: RelationshipExpandDirection,
    ): RelationshipExpandStatus => {
      const key = expandKey(nodeId, direction);
      if (loading.has(key)) {
        return "loading";
      }
      return expanded.has(key) ? "expanded" : "collapsed";
    },
    [expanded, loading],
  );

  const { layoutedNodes, layoutedEdges } = useMemo(() => {
    const mergedEdges = mergeRelationshipEdgeRecords(visibleRecords);

    const flowEdges: RelationshipEdge[] = mergedEdges.map((merged) => {
      const { id, source, target, records } = merged;
      // An edge touching a deleted item is a broken reference, so it is muted and dashed. Merged records
      // share both endpoints, so they are broken or whole together - there is no mixed case to resolve.
      const broken =
        visibleItems.get(source)?.isMissing === true ||
        visibleItems.get(target)?.isMissing === true;
      const color = broken ? MISSING_COLOR : REFERENCE_EDGE_COLOR;

      return {
        id,
        type: "relationshipEdge" as const,
        source,
        target,
        data: {
          // Hiding labels empties the entries rather than only skipping the chip. The same entries are
          // what `layout` measures to reserve label space in Dagre, so an empty list drops the chip and
          // the gap Dagre was holding for it together - leaving the reservation in place would keep the
          // ranks apart for a label nobody is drawing.
          entries: showEdgeLabels
            ? records.map((record) => ({
                label: record.label,
                path: record.path,
              }))
            : [],
          broken,
        },
        style: {
          stroke: color,
          strokeWidth: 1.5,
          strokeDasharray: broken ? "6 4" : undefined,
        },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color,
        },
      };
    });

    const flowNodes: RelationshipNode[] = Array.from(visibleItems.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([id, item]) => ({
        id,
        type: "relationshipNode" as const,
        position: { x: 0, y: 0 },
        data: {
          item,
          isRoot: id === rootId,
          horizontal: direction === "LR",
          canExpand:
            Boolean(item.itemId) && !item.isMissing && !item.isRestricted,
          incomingStatus: expandStatus(id, "incoming"),
          outgoingStatus: expandStatus(id, "outgoing"),
          onExpand: (direction: RelationshipExpandDirection) =>
            handleExpand(item, direction),
        },
      }));

    return {
      // Only layout sees the fields: they order the nodes within a rank and change nothing about the
      // edges themselves.
      layoutedNodes: layout(
        flowNodes,
        flowEdges,
        new Map(mergedEdges.map((merged) => [merged.id, merged.field])),
        direction,
      ),
      layoutedEdges: flowEdges,
    };
  }, [
    visibleItems,
    visibleRecords,
    rootId,
    expandStatus,
    handleExpand,
    direction,
    showEdgeLabels,
  ]);

  const [nodes, setNodes, onNodesChange] =
    useNodesState<RelationshipNode>(layoutedNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>(layoutedEdges);

  useEffect(() => {
    setNodes(layoutedNodes);
    setEdges(layoutedEdges);
  }, [layoutedNodes, layoutedEdges, setNodes, setEdges]);

  const onFitView = useCallback(() => fitView({ duration: 300 }), [fitView]);

  return (
    <div className="cmg-relationships-root">
      <div className="cmg-relationships-canvas">
        <RelationshipNodeSearchContext.Provider
          value={search.trim().toLowerCase()}
        >
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            nodesConnectable={false}
            minZoom={0.05}
            fitView
          >
            <Panel position="top-left" className="cmg-relationships-panel">
              <div className="cmg-relationships-actions">
                <Button
                  icon={direction === "LR" ? "xp-arrows-v" : "xp-arrows-h"}
                  title={
                    direction === "LR" ? "Vertical layout" : "Horizontal layout"
                  }
                  aria-label={
                    direction === "LR" ? "Vertical layout" : "Horizontal layout"
                  }
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={() =>
                    setDirection((current) => (current === "LR" ? "TB" : "LR"))
                  }
                />
                <Button
                  icon={showEdgeLabels ? "xp-eye-slash" : "xp-eye"}
                  title={
                    showEdgeLabels
                      ? "Hide reference labels"
                      : "Show reference labels"
                  }
                  aria-label={
                    showEdgeLabels
                      ? "Hide reference labels"
                      : "Show reference labels"
                  }
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  active={showEdgeLabels}
                  onClick={() => setShowEdgeLabels((current) => !current)}
                />
                <Button
                  icon="xp-bullseye"
                  title="Fit view"
                  aria-label="Fit view"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={onFitView}
                />
                <Button
                  icon="xp-rotate-right"
                  title="Reset graph"
                  aria-label="Reset graph"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={onReset}
                />
                <Button
                  icon="xp-arrow-down-line"
                  title="Export JSON"
                  aria-label="Export JSON"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={() =>
                    exportJson(
                      items.get(rootId) ?? graph.rootItem,
                      rootId,
                      visibleItems,
                      visibleRecords,
                      graph.languageCode ?? null,
                    )
                  }
                />
              </div>
            </Panel>

            <Panel
              position="top-right"
              className="cmg-relationships-panel cmg-relationships-panel--filters"
            >
              <input
                className="cmg-relationships-search"
                type="search"
                aria-label="Highlight items by name"
                placeholder="Highlight by name..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </Panel>
            {(graph.truncations?.length ?? 0) > 0 && (
              <Panel
                position="bottom-center"
                className="cmg-relationships-panel cmg-relationships-truncation"
                role="status"
              >
                {graph.truncations?.map((truncation) => (
                  <div key={truncation.direction}>
                    {truncationText(truncation)}
                  </div>
                ))}
              </Panel>
            )}
            {nodes.length === 0 && (
              <div className="cmg-relationships-empty">
                No relationships to display.
              </div>
            )}
            {minimapExpanded && (
              <MiniMap
                pannable
                zoomable
                nodeColor={minimapNodeColor}
                className="cmg-relationships-minimap"
                style={MINIMAP_STYLE}
                ariaLabel="Relationship graph minimap"
              />
            )}
            <Panel
              position="bottom-right"
              className="cmg-relationships-panel cmg-relationships-minimap-toggle"
            >
              <Button
                icon={minimapExpanded ? "xp-modal-minimize" : "xp-map"}
                title={minimapExpanded ? "Hide minimap" : "Show minimap"}
                aria-label={minimapExpanded ? "Hide minimap" : "Show minimap"}
                aria-expanded={minimapExpanded}
                size={ButtonSize.XS}
                color={ButtonColor.Quinary}
                active={minimapExpanded}
                onClick={() => setMinimapExpanded((expanded) => !expanded)}
              />
            </Panel>
            <Controls />
            <Background />
          </ReactFlow>
        </RelationshipNodeSearchContext.Provider>
      </div>
    </div>
  );
};

export const ContentItemRelationshipsTemplate = ({
  graph,
}: ContentItemRelationshipsTemplateProps) => {
  if (!graph?.rootItem) {
    return (
      <div className="cmg-relationships-root">
        <p className="cmg-relationships__missing">
          Relationship information is unavailable for this item.
        </p>
      </div>
    );
  }

  return (
    <ReactFlowProvider>
      <ContentItemRelationshipsGraph graph={graph} />
    </ReactFlowProvider>
  );
};
