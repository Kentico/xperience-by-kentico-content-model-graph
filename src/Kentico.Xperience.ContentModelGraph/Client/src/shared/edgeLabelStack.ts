/**
 * Draws edges whose source is their own target as loops around the node, and keeps the chips of several such
 * loops on one node from being drawn on top of one another. Edges between two different nodes are routed by
 * `edgeRoute.ts`.
 *
 * Pure and dependency-free so it runs under Node's test runner without a bundler.
 */

/** The space between two stacked chips, in pixels. */
export const EDGE_LABEL_STACK_GAP = 4;

/** A node's box on the canvas, in flow coordinates. */
export interface EdgeLabelNodeBox {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/**
 * Draws an edge whose source is its own target - a content type with a field that allows its own type, or an
 * item selected in its own field.
 *
 * A bezier from a node's source handle to its own target handle runs straight back across the node, so the
 * edge vanishes under it and its chip lands on the node. A self-loop instead leaves the source handle, runs
 * around the far side of the node and comes back into the target handle:
 *
 * - `LR` (handles left and right): out of the right handle, under the node, into the left handle. The chip
 *   hangs below the node, centred on it, covering the loop's bottom run.
 * - `TB` (handles top and bottom): out of the bottom handle, up the right of the node, into the top handle.
 *   The chip stands to the right of the node, covering the loop's side run.
 *
 * Dagre keeps self-loops out of ranking and reserves a slot for each one's label beside its node - below it
 * under `LR`, to its right under `TB` - which is the side the loop is drawn on. Several self-loops on one
 * node (a class referencing itself through a content field and an object field) are nested, each a little
 * further out, and their chips are stacked outwards from the node rather than drawn on top of each other.
 */

/** How far the first loop stands off its node, and how far it swings out past its handles. */
export const SELF_LOOP_REACH = 24;

/** How much further out each further loop on the same node is drawn. */
export const SELF_LOOP_SPACING = 12;

/** The space between the node and the first chip of its self-loops. */
export const SELF_LOOP_LABEL_GAP = 8;

export interface SelfLoopInput {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  /** The chip's estimated height; zero when the edge draws no chip. */
  readonly height: number;
}

/**
 * One self-loop's place among the self-loops of its node. `index` nests the loops; `offset` is how far into
 * the node's chip stack this loop's chip starts, `height` is its own estimated height and `stackHeight` the
 * whole stack's, so each chip can be placed without knowing about the others.
 */
export interface SelfLoopSlot {
  readonly index: number;
  readonly offset: number;
  readonly height: number;
  readonly stackHeight: number;
}

export const isSelfLoop = (edge: { source: string; target: string }) =>
  edge.source === edge.target;

/**
 * Assigns every self-loop its slot among the self-loops of the same node, in the order given. Edges between
 * two different nodes are ignored. An unlabelled loop still takes an index, so it nests, but no room in the
 * chip stack.
 */
export const stackSelfLoops = (
  edges: readonly SelfLoopInput[],
): ReadonlyMap<string, SelfLoopSlot> => {
  const byNode = new Map<string, SelfLoopInput[]>();

  for (const edge of edges) {
    if (!isSelfLoop(edge)) {
      continue;
    }

    const group = byNode.get(edge.source);

    if (group) {
      group.push(edge);
    } else {
      byNode.set(edge.source, [edge]);
    }
  }

  const slots = new Map<string, SelfLoopSlot>();

  for (const group of byNode.values()) {
    const labelled = group.filter((edge) => edge.height > 0);
    const stackHeight =
      labelled.reduce((total, edge) => total + edge.height, 0) +
      Math.max(labelled.length - 1, 0) * EDGE_LABEL_STACK_GAP;
    let offset = 0;

    group.forEach((edge, index) => {
      const height = Math.max(edge.height, 0);

      slots.set(edge.id, { index, offset, height, stackHeight });

      if (height > 0) {
        offset += height + EDGE_LABEL_STACK_GAP;
      }
    });
  }

  return slots;
};

export interface SelfLoopGeometryInput {
  /** The node the loop leaves and returns to. */
  readonly box: EdgeLabelNodeBox;
  /** `LR` layout: handles on the left and right. Otherwise `TB`: handles on the top and bottom. */
  readonly horizontal: boolean;
  readonly sourceX: number;
  readonly sourceY: number;
  readonly targetX: number;
  readonly targetY: number;
  /** Absent for a loop that is alone on its node. */
  readonly slot?: SelfLoopSlot;
}

const DEFAULT_SLOT: SelfLoopSlot = {
  index: 0,
  offset: 0,
  height: 0,
  stackHeight: 0,
};

/**
 * The loop's SVG path and the CSS transform that puts its chip on it. The path starts and ends on the
 * handle coordinates ReactFlow reports, so the arrowhead meets the target handle exactly as a normal edge's
 * does, and it never crosses the node's box.
 */
export const selfLoopGeometry = ({
  box,
  horizontal,
  sourceX,
  sourceY,
  targetX,
  targetY,
  slot = DEFAULT_SLOT,
}: SelfLoopGeometryInput) => {
  const reach = SELF_LOOP_REACH + slot.index * SELF_LOOP_SPACING;

  if (horizontal) {
    const bottom = box.y + box.height;
    const chipTop = bottom + SELF_LOOP_LABEL_GAP + slot.offset;
    // A labelled loop runs through the middle of its own chip, so each chip of a stack sits on its loop.
    const far = slot.height > 0 ? chipTop + slot.height / 2 : bottom + reach;

    return {
      path:
        `M ${sourceX},${sourceY} ` +
        `C ${sourceX + reach},${sourceY} ${sourceX + reach},${far} ${sourceX},${far} ` +
        `L ${targetX},${far} ` +
        `C ${targetX - reach},${far} ${targetX - reach},${targetY} ${targetX},${targetY}`,
      labelTransform: `translate(-50%, 0%) translate(${box.x + box.width / 2}px, ${chipTop}px)`,
    };
  }

  const right = box.x + box.width;
  const far = right + reach;
  const chipTop = box.y + box.height / 2 - slot.stackHeight / 2 + slot.offset;

  return {
    path:
      `M ${sourceX},${sourceY} ` +
      `C ${sourceX},${sourceY + reach} ${far},${sourceY + reach} ${far},${sourceY} ` +
      `L ${far},${targetY} ` +
      `C ${far},${targetY - reach} ${targetX},${targetY - reach} ${targetX},${targetY}`,
    labelTransform:
      slot.stackHeight > 0
        ? `translate(0%, 0%) translate(${right + SELF_LOOP_LABEL_GAP}px, ${chipTop}px)`
        : `translate(0%, -50%) translate(${right + SELF_LOOP_LABEL_GAP}px, ${box.y + box.height / 2}px)`,
  };
};
