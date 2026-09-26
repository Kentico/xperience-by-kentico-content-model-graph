import {
  BaseEdge,
  EdgeLabelRenderer,
  type Edge,
  type EdgeProps,
} from "@xyflow/react";

import {
  EDGE_LABEL_LINE_HEIGHT,
  EDGE_LABEL_VERTICAL_CHROME,
  estimateEdgeLabelSegment,
} from "../shared/edgeLabel";
import type { EdgeLabelSlot, SelfLoopSlot } from "../shared/edgeLabelStack";
import { useEdgeGeometry } from "../shared/useEdgeLabelTransform";

export interface ClassEdgeData extends Record<string, unknown> {
  /**
   * The field the reference was declared on, or "Schema" for a schema
   * assignment. Absent while the "Show field names" toggle is off, which is the
   * only reason an edge here carries no label: an edge stands for exactly one
   * relationship, so there is never more than one label to show.
   */
  readonly label?: string;
  /**
   * Set only when another labelled edge joins the same pair of nodes, in
   * either direction: the chips of such a group would otherwise be drawn on
   * top of each other, so they are stacked instead - see `edgeLabelStack.ts`.
   */
  readonly labelSlot?: EdgeLabelSlot;
  /**
   * Set only on an edge from a class to itself - a field that allows its own
   * content type - which is drawn as a loop around the node, nested and
   * stacked with any other self-loops of the same node.
   */
  readonly selfLoopSlot?: SelfLoopSlot;
}

export type ClassEdge = Edge<ClassEdgeData, "classEdge">;

/**
 * Dagre reserves no room for an edge label unless the edge carries its
 * dimensions, so every edge passes the chip's estimated box through to the
 * layout - without it the chips are drawn across the nodes on either side. A
 * label here is a single field name with no forced breaks, so one wrapped run
 * of text is the whole chip.
 *
 * An edge with no label returns a zero box, so hiding field names reserves
 * nothing and the layout closes up rather than keeping gaps for chips that are
 * never drawn.
 */
export const estimateClassEdgeLabelSize = (label: string | undefined) => {
  if (!label) {
    return { width: 0, height: 0 };
  }

  const { width, lines } = estimateEdgeLabelSegment(label);

  return {
    width,
    height: lines * EDGE_LABEL_LINE_HEIGHT + EDGE_LABEL_VERTICAL_CHROME,
  };
};

/**
 * The label is rendered as HTML rather than through ReactFlow's built-in SVG
 * edge label so it can be drawn as the same bordered chip the relationships
 * graph uses: SVG text with a background rectangle has no border, does not
 * wrap, and has nowhere to hang a tooltip for the text a clamp cut off.
 */
export const ClassEdgeComponent = ({
  id,
  source,
  target,
  sourceX,
  sourceY,
  sourcePosition,
  targetX,
  targetY,
  targetPosition,
  markerEnd,
  style,
  data,
}: EdgeProps<ClassEdge>) => {
  const { path: edgePath, labelTransform: transform } = useEdgeGeometry({
    source,
    target,
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
    labelSlot: data?.labelSlot,
    selfLoopSlot: data?.selfLoopSlot,
  });

  const label = data?.label;

  return (
    <>
      <BaseEdge id={id} path={edgePath} markerEnd={markerEnd} style={style} />
      {label && (
        <EdgeLabelRenderer>
          <div
            className="cmg-edge-label nodrag nopan"
            style={{
              transform,
            }}
            // A long field name wraps to two lines and is clamped after that,
            // so the full text has to stay reachable from somewhere.
            title={label}
          >
            <div className="cmg-edge-label__line">{label}</div>
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  );
};
