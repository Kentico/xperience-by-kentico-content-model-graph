import { useInternalNode, type InternalNode } from "@xyflow/react";

import {
  edgeLabelStackAnchor,
  edgeLabelTransform,
  type EdgeLabelNodeBox,
  type EdgeLabelSlot,
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
