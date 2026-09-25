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
  CLASS_NODE_WIDTH,
  ClassNodeComponent,
  ClassNodeSearchContext,
  classNodeHeight,
  type ClassNode,
} from "./ClassNode";
import {
  ClassEdgeComponent,
  estimateClassEdgeLabelSize,
  type ClassEdge,
} from "./ClassEdge";
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
const edgeTypes = { classEdge: ClassEdgeComponent };
const minimapNodeColor = (node: ClassNode) => nodeColor(node.data.kind);

// ReactFlow's minimap defaults to 200x150, a sizeable bite out of a canvas this
// graph needs. 140x105 keeps the 4:3 shape at about half the area. The size has
// to travel through `style` rather than the stylesheet: MiniMap reads
// `style.width` / `style.height` to compute the SVG viewBox, so CSS-only sizing
// draws a full-size map inside a smaller box.
const MINIMAP_STYLE = { width: 140, height: 105 };

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

/*
 * `fitView` measures nodes only: panels, controls and the minimap are DOM
 * overlays that never reach the bounds calculation. Its default `padding` of
 * 0.1 then leaves the same gutter on every side (~4.5% of the canvas, so ~40px
 * at this graph's minimum width). The left-hand action column is narrow enough
 * to sit inside that gutter, which is why it never collides with the nodes;
 * anything wider on the right does collide. Reserving each side in pixels keeps
 * both sets of controls clear of the graph at any canvas size.
 */
const FIT_VIEW_PADDING = {
  top: "16px",
  right: "56px",
  bottom: "48px",
  left: "56px",
} as const;

// The expanded filters panel is the 200px legend plus its offset from the edge.
const FIT_VIEW_PADDING_FILTERS_EXPANDED = {
  ...FIT_VIEW_PADDING,
  right: "232px",
} as const;

const fitViewOptions = { padding: FIT_VIEW_PADDING };

const layout = (
  nodes: ClassNode[],
  edges: ClassEdge[],
  direction: "LR" | "TB",
) => {
  const graph = new Dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  graph.setGraph({
    rankdir: direction,
    ranksep: 160,
    nodesep: 24,
    edgesep: 24,
  });

  // Every node is the same size except the one the contextual pages focus on,
  // which wears a ribbon the others do not. The heights are kept so the same
  // value offsets the node's centre below.
  const heights = new Map(
    nodes.map((node) => [node.id, classNodeHeight(node.data.isCurrent)]),
  );

  nodes.forEach((node) =>
    graph.setNode(node.id, {
      width: CLASS_NODE_WIDTH,
      height: heights.get(node.id) ?? 0,
    }),
  );
  // Dagre reserves no space for an edge label unless the edge carries its
  // dimensions, which is what let the relationships graph draw its labels
  // across the nodes on either side. The bordered chips are wider than the SVG
  // text this graph used to draw, so the reservation is what keeps them off the
  // nodes. With field names hidden the estimate is a zero box, so nothing is
  // reserved for labels that are never drawn.
  //
  // Two fields of one class pointing at the same class are two edges between
  // the same pair of nodes, and this is not a multigraph, so the second
  // reservation would replace the first: the larger of the two is kept, which
  // is the one that has to fit.
  edges.forEach((edge) => {
    const { width, height } = estimateClassEdgeLabelSize(edge.data?.label);
    const reserved = graph.edge(edge.source, edge.target);

    graph.setEdge(edge.source, edge.target, {
      width: Math.max(width, reserved?.width ?? 0),
      height: Math.max(height, reserved?.height ?? 0),
      labelpos: "c",
    });
  });

  Dagre.layout(graph);

  return nodes.map((node) => {
    const positioned = graph.node(node.id);

    return {
      ...node,
      position: {
        x: positioned.x - CLASS_NODE_WIDTH / 2,
        y: positioned.y - (heights.get(node.id) ?? 0) / 2,
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
  // Filters are set once and then left alone, so the panel starts collapsed to
  // its toggle and gives the canvas back the right-hand column it occupied.
  const [filtersExpanded, setFiltersExpanded] = useState(false);
  // The minimap is an occasional orientation aid, not something to read while
  // working, so it starts collapsed to its toggle in the opposite corner.
  const [minimapExpanded, setMinimapExpanded] = useState(false);

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

    const flowEdges: ClassEdge[] = data.edges
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
          type: "classEdge" as const,
          source: edge.source,
          target: edge.target,
          data: {
            // Every edge kind hides its label until the toolbar toggle is on,
            // so the toggle uniformly governs all labels. Schema assignments
            // name the relationship rather than a field, hence the fixed text.
            label: showFieldNames
              ? edge.kind === "schemaAssignment"
                ? "Schema"
                : edge.label
              : undefined,
          },
          style: {
            stroke: style?.color,
            strokeDasharray: style?.dashed ? "6 4" : undefined,
            strokeWidth: 1.5,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: style?.color },
        };
      });

    // Absent on the unfiltered graph, which has no focal node - the three
    // contextual pages are the only ones that filter to one node's
    // neighbourhood and so the only ones that name it.
    const focalNodeId = data.focalNodeId
      ? data.focalNodeId.toLowerCase()
      : undefined;

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
          schemaFieldCount: node.schemaFieldCount ?? undefined,
          horizontal: direction === "LR",
          // Only the contextual pages send a focal node id; on the unfiltered
          // graph it is absent and no node is ever marked. The server filters
          // the neighbourhood case-insensitively, so the comparison does too.
          isCurrent:
            focalNodeId !== undefined && node.id.toLowerCase() === focalNodeId,
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
  const [edges, setEdges, onEdgesChange] =
    useEdgesState<ClassEdge>(layoutedEdges);

  useEffect(() => {
    setNodes(layoutedNodes);
    setEdges(layoutedEdges);
  }, [layoutedNodes, layoutedEdges, setNodes, setEdges]);

  const onFitView = useCallback(
    () =>
      fitView({
        duration: 300,
        padding: filtersExpanded
          ? FIT_VIEW_PADDING_FILTERS_EXPANDED
          : FIT_VIEW_PADDING,
      }),
    [fitView, filtersExpanded],
  );

  return (
    <div className="cmg-root">
      <div className="cmg-canvas">
        <ClassNodeSearchContext.Provider value={search.trim().toLowerCase()}>
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
            fitViewOptions={fitViewOptions}
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
              <div className="cmg-filters-toggle">
                <Button
                  icon={filtersExpanded ? "xp-modal-minimize" : "xp-filter-1"}
                  title={filtersExpanded ? "Hide filters" : "Show filters"}
                  aria-label={filtersExpanded ? "Hide filters" : "Show filters"}
                  aria-expanded={filtersExpanded}
                  size={ButtonSize.XS}
                  color={ButtonColor.Quinary}
                  active={filtersExpanded}
                  onClick={() => setFiltersExpanded((expanded) => !expanded)}
                />
              </div>

              {filtersExpanded && (
                <>
                  <input
                    className="cmg-search"
                    type="search"
                    aria-label="Highlight classes by name"
                    placeholder="Highlight by name..."
                    value={search}
                    name="search"
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
                          const selected =
                            visibleSystemObjectTypeGroups.includes(group.group);

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
                      <div className="cmg-filter-legend__title">
                        Relationships
                      </div>
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
                </>
              )}
            </Panel>
            {nodes.length === 0 && (
              <div className="cmg-empty">
                No classes match the current filters.
              </div>
            )}
            {minimapExpanded && (
              <MiniMap
                pannable
                zoomable
                nodeColor={minimapNodeColor}
                className="cmg-minimap"
                style={MINIMAP_STYLE}
                ariaLabel="Content model graph minimap"
              />
            )}
            <Panel
              position="bottom-right"
              className="cmg-panel cmg-minimap-toggle"
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
