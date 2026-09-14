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
import { usePageCommand } from "@kentico/xperience-admin-base";

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
import type {
  ContentItemRelationshipGraphDto,
  ContentItemRelationshipItemDto,
} from "./model";

interface ContentItemRelationshipsTemplateProps {
  readonly graph?: ContentItemRelationshipGraphDto | null;
}

interface ExpandRelationshipsArgs {
  readonly itemId: number;
}

interface RelationshipEdgeRecord {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  readonly label: string;
  readonly origin: string;
}

const nodeTypes = { relationshipNode: RelationshipNodeComponent };

const REFERENCE_EDGE_COLOR = "#3d5dff";

const itemNodeId = (item: ContentItemRelationshipItemDto) =>
  item.identifier ?? item.itemId?.toString() ?? item.codeName;

const expandKey = (nodeId: string, direction: RelationshipExpandDirection) =>
  `${nodeId}:${direction}`;

// Same reference can be discovered from either endpoint's fetch with a different relationship id,
// so edges are deduplicated by their actual endpoints and field rather than that id.
const edgeContentKey = (
  source: string,
  target: string,
  fieldCodeName: string,
  fieldLabel: string,
) => `${source}=>${target}:${fieldCodeName || fieldLabel}`;

const layout = (nodes: RelationshipNode[], edges: Edge[]) => {
  const graph = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  graph.setGraph({
    rankdir: "LR",
    ranksep: 160,
    nodesep: 32,
    edgesep: 24,
  });

  nodes.forEach((node) =>
    graph.setNode(node.id, {
      width: RELATIONSHIP_NODE_WIDTH,
      height: estimateRelationshipNodeHeight(
        node.data.item,
        node.data.canExpand,
        node.data.isRoot,
      ),
    }),
  );
  edges.forEach((edge) => graph.setEdge(edge.source, edge.target));

  Dagre.layout(graph);

  return nodes.map((node) => {
    const positioned = graph.node(node.id);

    return {
      ...node,
      position: {
        x: positioned.x - RELATIONSHIP_NODE_WIDTH / 2,
        y:
          positioned.y -
          estimateRelationshipNodeHeight(
            node.data.item,
            node.data.canExpand,
            node.data.isRoot,
          ) /
            2,
      },
    };
  });
};

const initialItems = (graph: ContentItemRelationshipGraphDto) => {
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

const initialEdges = (graph: ContentItemRelationshipGraphDto) => {
  const rootId = itemNodeId(graph.rootItem);
  const edges = new Map<string, RelationshipEdgeRecord>();

  for (const relationship of graph.incoming ?? []) {
    const source = itemNodeId(relationship.relatedItem);
    const key = edgeContentKey(
      source,
      rootId,
      relationship.fieldCodeName,
      relationship.fieldLabel,
    );
    edges.set(key, {
      id: relationship.id,
      source,
      target: rootId,
      label: relationship.fieldLabel || relationship.fieldCodeName,
      origin: expandKey(rootId, "incoming"),
    });
  }

  for (const relationship of graph.outgoing ?? []) {
    const target = itemNodeId(relationship.relatedItem);
    const key = edgeContentKey(
      rootId,
      target,
      relationship.fieldCodeName,
      relationship.fieldLabel,
    );
    edges.set(key, {
      id: relationship.id,
      source: rootId,
      target,
      label: relationship.fieldLabel || relationship.fieldCodeName,
      origin: expandKey(rootId, "outgoing"),
    });
  }

  return edges;
};

const ContentItemRelationshipsGraph = ({
  graph,
}: {
  readonly graph: ContentItemRelationshipGraphDto;
}) => {
  const { fitView } = useReactFlow();
  const [search, setSearch] = useState("");
  const rootId = useMemo(() => itemNodeId(graph.rootItem), [graph.rootItem]);

  const [items, setItems] = useState(() => initialItems(graph));
  const [edgeRecords, setEdgeRecords] = useState(() => initialEdges(graph));
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(
    () =>
      new Set([expandKey(rootId, "incoming"), expandKey(rootId, "outgoing")]),
  );
  const [loading, setLoading] = useState<ReadonlySet<string>>(new Set());
  const pendingExpansions = useRef<
    { nodeId: string; direction: RelationshipExpandDirection }[]
  >([]);

  const mergeExpansion = useCallback(
    (
      nodeId: string,
      direction: RelationshipExpandDirection,
      response: ContentItemRelationshipGraphDto,
    ) => {
      const key = expandKey(nodeId, direction);
      const relationships =
        direction === "incoming"
          ? (response.incoming ?? [])
          : (response.outgoing ?? []);

      setItems((current) => {
        const next = new Map(current);
        for (const relationship of relationships) {
          next.set(
            itemNodeId(relationship.relatedItem),
            relationship.relatedItem,
          );
        }
        return next;
      });

      setEdgeRecords((current) => {
        const next = new Map(current);
        for (const [contentKey, record] of current) {
          if (record.origin === key) {
            next.delete(contentKey);
          }
        }
        for (const relationship of relationships) {
          const relatedId = itemNodeId(relationship.relatedItem);
          const source = direction === "incoming" ? relatedId : nodeId;
          const target = direction === "incoming" ? nodeId : relatedId;
          const contentKey = edgeContentKey(
            source,
            target,
            relationship.fieldCodeName,
            relationship.fieldLabel,
          );
          next.set(contentKey, {
            id: relationship.id,
            source,
            target,
            label: relationship.fieldLabel || relationship.fieldCodeName,
            origin: key,
          });
        }
        return next;
      });

      setExpanded((current) => new Set(current).add(key));
    },
    [],
  );

  const { execute: expandRelationships } = usePageCommand<
    ContentItemRelationshipGraphDto,
    ExpandRelationshipsArgs
  >("ExpandRelationships", {
    after: (response) => {
      const context = pendingExpansions.current.shift();
      if (context && response) {
        mergeExpansion(context.nodeId, context.direction, response);
      }
    },
  });

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

      setLoading((current) => new Set(current).add(key));
      pendingExpansions.current.push({ nodeId, direction });

      try {
        await expandRelationships({ itemId: item.itemId });
      } finally {
        setLoading((current) => {
          const next = new Set(current);
          next.delete(key);
          return next;
        });
      }
    },
    [expandRelationships],
  );

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
    // Sort by id so layout is deterministic regardless of Map mutation order from expand/refresh.
    const flowEdges: Edge[] = Array.from(edgeRecords.values())
      .sort((a, b) => a.id.localeCompare(b.id))
      .map((record) => ({
        id: record.id,
        source: record.source,
        target: record.target,
        label: record.label,
        labelBgPadding: [4, 2] as [number, number],
        labelBgBorderRadius: 4,
        style: { stroke: REFERENCE_EDGE_COLOR, strokeWidth: 1.5 },
        markerEnd: {
          type: MarkerType.ArrowClosed,
          color: REFERENCE_EDGE_COLOR,
        },
      }));

    const flowNodes: RelationshipNode[] = Array.from(items.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([id, item]) => ({
        id,
        type: "relationshipNode" as const,
        position: { x: 0, y: 0 },
        data: {
          item,
          isRoot: id === rootId,
          horizontal: true,
          canExpand: Boolean(item.itemId),
          incomingStatus: expandStatus(id, "incoming"),
          outgoingStatus: expandStatus(id, "outgoing"),
          onExpand: (direction: RelationshipExpandDirection) =>
            handleExpand(item, direction),
        },
      }));

    return {
      layoutedNodes: layout(flowNodes, flowEdges),
      layoutedEdges: flowEdges,
    };
  }, [items, edgeRecords, rootId, expandStatus, handleExpand]);

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
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            nodesConnectable={false}
            minZoom={0.05}
            fitView
          >
            <Panel position="top-left" className="cmg-relationships-panel">
              <div className="cmg-relationships-actions">
                <Button
                  icon="xp-bullseye"
                  title="Fit view"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={onFitView}
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
                placeholder="Highlight by name..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </Panel>
            {nodes.length === 0 && (
              <div className="cmg-relationships-empty">
                No relationships to display.
              </div>
            )}
            <MiniMap pannable zoomable />
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
