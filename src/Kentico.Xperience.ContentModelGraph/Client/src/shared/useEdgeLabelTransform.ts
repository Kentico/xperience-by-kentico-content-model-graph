import {
  getBezierPath,
  Position,
  useInternalNode,
  type InternalNode,
} from "@xyflow/react";

import {
  edgeLabelStackAnchor,
  edgeLabelTransform,
  selfLoopGeometry,
  type EdgeLabelNodeBox,
  type EdgeLabelSlot,
  type SelfLoopSlot,
} from "./edgeLabelStack";

const boxOf = (node: InternalNode | undefined): EdgeLabelNodeBox | undefined =>
  node?.measured.width !== undefined && node.measured.height !== undefined
    ? {
        x: node.internals.positionAbsolute.x,
        y: node.internals.positionAbsolute.y,
        width: node.measured.width,
        height: node.measured.height,
      }
    : undefined;

/**
 * The transform for an edge's label chip. An edge with no slot is alone between its pair of nodes and is
 * centred on its own curve (`labelX`, `labelY`); a slotted edge shares its pair with other labelled edges
 * and takes its place in a stack centred between the two nodes - see `edgeLabelStack.ts`.
 */
export const useEdgeLabelTransform = (
  source: string,
  target: string,
  labelX: number,
  labelY: number,
  slot: EdgeLabelSlot | undefined,
) => {
  const sourceNode = useInternalNode(source);
  const targetNode = useInternalNode(target);

  if (!slot) {
    return edgeLabelTransform(labelX, labelY);
  }

  const anchor = edgeLabelStackAnchor(boxOf(sourceNode), boxOf(targetNode));

  return edgeLabelTransform(anchor?.x ?? labelX, anchor?.y ?? labelY, slot);
};

export interface EdgeGeometryInput {
  readonly source: string;
  readonly target: string;
  readonly sourceX: number;
  readonly sourceY: number;
  readonly sourcePosition: Position;
  readonly targetX: number;
  readonly targetY: number;
  readonly targetPosition: Position;
  readonly labelSlot?: EdgeLabelSlot;
  readonly selfLoopSlot?: SelfLoopSlot;
}

/**
 * The path an edge is drawn along and the transform for its label chip. An edge between two nodes is a
 * bezier with its chip centred on it, or stacked with the chips of the other edges between the same pair;
 * an edge from a node to itself is a loop around the node with its chip on the loop - a bezier from a
 * node's source handle back to its own target handle would run straight across the node.
 */
export const useEdgeGeometry = ({
  source,
  target,
  sourceX,
  sourceY,
  sourcePosition,
  targetX,
  targetY,
  targetPosition,
  labelSlot,
  selfLoopSlot,
}: EdgeGeometryInput) => {
  const [bezierPath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });
  const labelTransform = useEdgeLabelTransform(
    source,
    target,
    labelX,
    labelY,
    labelSlot,
  );
  const box = boxOf(useInternalNode(source));

  if (source !== target || !box) {
    return { path: bezierPath, labelTransform };
  }

  return selfLoopGeometry({
    box,
    // The handles sit on the left and right of a node under `LR`, on its top and bottom under `TB`.
    horizontal:
      sourcePosition === Position.Left || sourcePosition === Position.Right,
    sourceX,
    sourceY,
    targetX,
    targetY,
    slot: selfLoopSlot,
  });
};
