import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  type Edge,
  type EdgeProps,
} from "@xyflow/react";

import {
  EDGE_LABEL_LINE_HEIGHT,
  EDGE_LABEL_VERTICAL_CHROME,
  estimateEdgeLabelSegment,
} from "../shared/edgeLabel";

export interface ClassEdgeData extends Record<string, unknown> {
  /**
   * The field the reference was declared on, or "Schema" for a schema
   * assignment. Absent while the "Show field names" toggle is off, which is the
   * only reason an edge here carries no label: an edge stands for exactly one
   * relationship, so there is never more than one label to show.
   */
  readonly label?: string;
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
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
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
              transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
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
