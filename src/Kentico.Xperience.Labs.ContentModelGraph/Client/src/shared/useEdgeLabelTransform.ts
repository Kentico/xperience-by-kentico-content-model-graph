import {
  getBezierPath,
  Position,
  useInternalNode,
  type InternalNode,
} from "@xyflow/react";

import {
  selfLoopGeometry,
  type EdgeLabelNodeBox,
  type SelfLoopSlot,
} from "./edgeLabelStack";
import { edgeLabelTransform, edgeRoute, type EdgeLane } from "./edgeRoute";

const boxOf = (node: InternalNode | undefined): EdgeLabelNodeBox | undefined =>
  node?.measured.width !== undefined && node.measured.height !== undefined
    ? {
        x: node.internals.positionAbsolute.x,
        y: node.internals.positionAbsolute.y,
        width: node.measured.width,
        height: node.measured.height,
      }
    : undefined;

export interface EdgeGeometryInput {
  readonly source: string;
  readonly target: string;
  readonly sourceX: number;
  readonly sourceY: number;
  readonly sourcePosition: Position;
  readonly targetX: number;
  readonly targetY: number;
  readonly targetPosition: Position;
  readonly lane?: EdgeLane;
  readonly selfLoopSlot?: SelfLoopSlot;
}

/**
 * The path an edge is drawn along and the transform for its label chip. An edge between two nodes runs
 * between the sides of the two nodes that face each other, bowed into its own lane when it shares the pair
 * with other edges - see `edgeRoute.ts`; an edge from a node to itself is a loop around the node with its
 * chip on the loop - see `edgeLabelStack.ts`. Both read the nodes' live positions, so they follow a drag.
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
  lane,
  selfLoopSlot,
}: EdgeGeometryInput) => {
  const sourceBox = boxOf(useInternalNode(source));
  const targetBox = boxOf(useInternalNode(target));
  // The handles sit on the left and right of a node under `LR`, on its top and bottom under `TB`.
  const horizontal =
    sourcePosition === Position.Left || sourcePosition === Position.Right;

  if (!sourceBox || !targetBox) {
    // Not measured yet: draw ReactFlow's own curve until the nodes are.
    const [path, labelX, labelY] = getBezierPath({
      sourceX,
      sourceY,
      sourcePosition,
      targetX,
      targetY,
      targetPosition,
    });

    return { path, labelTransform: edgeLabelTransform(labelX, labelY) };
  }

  if (source === target) {
    return selfLoopGeometry({
      box: sourceBox,
      horizontal,
      sourceX,
      sourceY,
      targetX,
      targetY,
      slot: selfLoopSlot,
    });
  }

  return edgeRoute({
    sourceBox,
    targetBox,
    horizontal,
    sourceX,
    sourceY,
    targetX,
    targetY,
    lane,
    reversed: source > target,
  });
};
