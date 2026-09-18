import Dagre from "@dagrejs/dagre";
import { useCallback, useEffect, useMemo, useState } from "react";
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
import "./ContentModelGraph.css";

import {
  CLASS_NODE_HEIGHT,
  CLASS_NODE_WIDTH,
  ClassNodeComponent,
  ClassNodeSearchContext,
  type ClassNode,
} from "./ClassNode";
import {
  edgeKinds,
  edgeStyle,
  nodeColor,
  nodeKinds,
  systemObjectTypeGroups,
  type EdgeKind,
  type GraphDataDto,
  type NodeKind,
  type SystemObjectTypeGroup,
} from "./model";

interface ContentModelGraphProps {
  readonly graph: GraphDataDto;
  readonly assemblyName: string;
  readonly showFieldNamesByDefault: boolean;
}

const nodeTypes = { classNode: ClassNodeComponent };
const minimapNodeColor = (node: ClassNode) => nodeColor(node.data.kind);

const Commands = {
  ResetGraph: "ResetGraph",
  ClearCache: "ClearCache",
};

const defaultNodeKinds: NodeKind[] = [
  "website",
  "reusable",
  "email",
  "headless",
  "schema",
  "taxonomy",
  "forms",
  "objectType",
];
const defaultSystemObjectTypeGroups: SystemObjectTypeGroup[] =
  systemObjectTypeGroups.map(({ group }) => group);
const defaultEdgeKinds: EdgeKind[] = [
  "schemaAssignment",
  "contentReference",
  "schemaReference",
  "objectReference",
  "taxonomyReference",
];

const layout = (nodes: ClassNode[], edges: Edge[], direction: "LR" | "TB") => {
  const graph = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  graph.setGraph({
    rankdir: direction,
    ranksep: 160,
    nodesep: 24,
    edgesep: 24,
  });

  nodes.forEach((node) =>
    graph.setNode(node.id, {
      width: CLASS_NODE_WIDTH,
      height: CLASS_NODE_HEIGHT,
    }),
  );
  edges.forEach((edge) => graph.setEdge(edge.source, edge.target));

  Dagre.layout(graph);

  return nodes.map((node) => {
    const positioned = graph.node(node.id);

    return {
      ...node,
      position: {
        x: positioned.x - CLASS_NODE_WIDTH / 2,
        y: positioned.y - CLASS_NODE_HEIGHT / 2,
      },
    };
  });
};

const toggle = <T,>(values: T[], value: T): T[] =>
  values.includes(value)
    ? values.filter((item) => item !== value)
    : [...values, value];

const createTimestamp = (date: Date) => {
  const parts = [date.getFullYear(), date.getMonth() + 1, date.getDate()];
  const time = [date.getHours(), date.getMinutes(), date.getSeconds()];

  return [...parts, ...time]
    .map((part) => String(part).padStart(2, "0"))
    .join("");
};

const exportJson = (data: GraphDataDto, assemblyName: string) => {
  const blob = new Blob([JSON.stringify(data, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");

  link.href = url;
  link.download = `${assemblyName}-${createTimestamp(new Date())}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
};

const ContentModelGraph = ({
  graph,
  assemblyName,
  showFieldNamesByDefault,
}: ContentModelGraphProps) => {
  const { fitView } = useReactFlow();
  const [data, setData] = useState(graph);
  const [visibleNodeKinds, setVisibleNodeKinds] =
    useState<NodeKind[]>(defaultNodeKinds);
  const [visibleSystemObjectTypeGroups, setVisibleSystemObjectTypeGroups] =
    useState<SystemObjectTypeGroup[]>(defaultSystemObjectTypeGroups);
  const [visibleEdgeKinds, setVisibleEdgeKinds] =
    useState<EdgeKind[]>(defaultEdgeKinds);
  const [direction, setDirection] = useState<"LR" | "TB">("LR");
  const [showFieldNames, setShowFieldNames] = useState(showFieldNamesByDefault);
  const [search, setSearch] = useState("");

  const { execute: resetGraph } = usePageCommand<GraphDataDto>(
    Commands.ResetGraph,
    {
      after: (response) => {
        if (response) {
          setData(response);
          setVisibleNodeKinds(defaultNodeKinds);
          setVisibleSystemObjectTypeGroups(defaultSystemObjectTypeGroups);
          setVisibleEdgeKinds(defaultEdgeKinds);
          setDirection("LR");
          setShowFieldNames(showFieldNamesByDefault);
          setSearch("");
        }
      },
    },
  );

  const { execute: clearCache } = usePageCommand<GraphDataDto>(
    Commands.ClearCache,
    {
      after: (response) => {
        if (response) {
          setData(response);
        }
      },
    },
  );

  const { layoutedNodes, layoutedEdges } = useMemo(() => {
    const included = new Set(
      data.nodes
        .filter(
          (node) =>
            visibleNodeKinds.includes(node.kind) &&
            (node.kind !== "systemObjectType" ||
              (node.systemObjectTypeGroup !== undefined &&
                visibleSystemObjectTypeGroups.includes(
                  node.systemObjectTypeGroup,
                ))),
        )
        .map((node) => node.id),
    );

    const flowEdges: Edge[] = data.edges
      .filter(
        (edge) =>
          visibleEdgeKinds.includes(edge.kind) &&
          included.has(edge.source) &&
          included.has(edge.target),
      )
      .map((edge) => {
        const style = edgeStyle(edge.kind);

        return {
          id: edge.id,
          source: edge.source,
          target: edge.target,
          label:
            edge.kind === "schemaAssignment"
              ? "Schema"
              : showFieldNames
                ? edge.label
                : undefined,
          labelBgPadding: [4, 2] as [number, number],
          labelBgBorderRadius: 4,
          style: {
            stroke: style?.color,
            strokeDasharray: style?.dashed ? "6 4" : undefined,
            strokeWidth: 1.5,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: style?.color },
        };
      });

    const flowNodes: ClassNode[] = data.nodes
      .filter((node) => included.has(node.id))
      .map((node) => ({
        id: node.id,
        type: "classNode" as const,
        position: { x: 0, y: 0 },
        data: {
          displayName: node.displayName,
          name: node.name,
          adminUrl: node.adminUrl,
          kind: node.kind,
          fieldCount: node.fieldCount,
          horizontal: direction === "LR",
        },
      }));

    return {
      layoutedNodes: layout(flowNodes, flowEdges, direction),
      layoutedEdges: flowEdges,
    };
  }, [
    data,
    visibleNodeKinds,
    visibleSystemObjectTypeGroups,
    visibleEdgeKinds,
    direction,
    showFieldNames,
  ]);

  const [nodes, setNodes, onNodesChange] =
    useNodesState<ClassNode>(layoutedNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>(layoutedEdges);

  useEffect(() => {
    setNodes(layoutedNodes);
    setEdges(layoutedEdges);
  }, [layoutedNodes, layoutedEdges, setNodes, setEdges]);

  const onFitView = useCallback(() => fitView({ duration: 300 }), [fitView]);

  return (
    <div className="cmg-root">
      <div className="cmg-canvas">
        <ClassNodeSearchContext.Provider value={search.trim().toLowerCase()}>
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
            <Panel position="top-left" className="cmg-panel">
              <div className="cmg-actions">
                <Button
                  icon={direction === "LR" ? "xp-arrows-v" : "xp-arrows-h"}
                  title={
                    direction === "LR" ? "Vertical layout" : "Horizontal layout"
                  }
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={() => setDirection(direction === "LR" ? "TB" : "LR")}
                />
                <Button
                  icon={showFieldNames ? "xp-eye-slash" : "xp-eye"}
                  title={
                    showFieldNames ? "Hide field names" : "Show field names"
                  }
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  active={showFieldNames}
                  onClick={() => setShowFieldNames((current) => !current)}
                />
                <Button
                  icon="xp-bullseye"
                  title="Fit view"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={onFitView}
                />
                <Button
                  icon="xp-rotate-right"
                  title="Reset graph"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={() => resetGraph()}
                />
                <Button
                  icon="xp-arrow-down-line"
                  title="Export JSON"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={() => exportJson(data, assemblyName)}
                />
                <Button
                  icon="xp-broom"
                  title="Clear cache"
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  onClick={() => clearCache()}
                />
              </div>
            </Panel>

            <Panel
              position="top-right"
              className="cmg-panel cmg-panel--filters"
            >
              <input
                className="cmg-search"
                type="search"
                placeholder="Highlight by name..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />

              <div className="cmg-filter-legend">
                <div className="cmg-filter-legend__heading">Filters</div>
                <div className="cmg-filter-legend__group">
                  <div className="cmg-filter-legend__title">Nodes</div>
                  {nodeKinds.map((kind) => {
                    const selected = visibleNodeKinds.includes(kind.kind);

                    return (
                      <button
                        key={kind.kind}
                        type="button"
                        className={`cmg-filter${selected ? "" : " cmg-filter--off"}`}
                        aria-pressed={selected}
                        onClick={() =>
                          setVisibleNodeKinds((current) =>
                            toggle(current, kind.kind),
                          )
                        }
                      >
                        <span
                          className="cmg-swatch"
                          style={{ background: kind.color }}
                        />
                        {kind.label}
                      </button>
                    );
                  })}
                  <div className="cmg-filter-legend__subgroup">
                    {systemObjectTypeGroups.map((group) => {
                      const selected = visibleSystemObjectTypeGroups.includes(
                        group.group,
                      );

                      return (
                        <button
                          key={group.group}
                          type="button"
                          className={`cmg-filter cmg-filter--subfilter${selected ? "" : " cmg-filter--off"}`}
                          aria-pressed={selected}
                          onClick={() =>
                            setVisibleSystemObjectTypeGroups((current) =>
                              toggle(current, group.group),
                            )
                          }
                        >
                          {group.label}
                        </button>
                      );
                    })}
                  </div>
                </div>

                <div className="cmg-filter-legend__group">
                  <div className="cmg-filter-legend__title">Relationships</div>
                  {edgeKinds.map((kind) => {
                    const selected = visibleEdgeKinds.includes(kind.kind);

                    return (
                      <button
                        key={kind.kind}
                        type="button"
                        className={`cmg-filter${selected ? "" : " cmg-filter--off"}`}
                        aria-pressed={selected}
                        onClick={() =>
                          setVisibleEdgeKinds((current) =>
                            toggle(current, kind.kind),
                          )
                        }
                      >
                        <span
                          className="cmg-swatch"
                          style={
                            kind.dashed
                              ? {
                                  background: `repeating-linear-gradient(90deg, ${kind.color} 0 3px, transparent 3px 6px)`,
                                }
                              : { background: kind.color }
                          }
                        />
                        {kind.label}
                      </button>
                    );
                  })}
                </div>
              </div>
            </Panel>
            {nodes.length === 0 && (
              <div className="cmg-empty">
                No classes match the current filters.
              </div>
            )}
            <MiniMap pannable zoomable nodeColor={minimapNodeColor} />
            <Controls />
            <Background />
          </ReactFlow>
        </ClassNodeSearchContext.Provider>
      </div>
    </div>
  );
};

export const ContentModelGraphTemplate = (props: ContentModelGraphProps) => (
  <ReactFlowProvider>
    <ContentModelGraph {...props} />
  </ReactFlowProvider>
);
