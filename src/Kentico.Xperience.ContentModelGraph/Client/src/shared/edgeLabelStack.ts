/**
 * Keeps the labels of edges that share a pair of nodes from being drawn on top of one another.
 *
 * Every edge puts its chip at the centre of its own bezier curve. Two edges between the same pair of nodes
 * therefore collide:
 *
 * - Two edges in the same direction (two fields of one class pointing at the same class) are the very same
 *   curve, so their chips sit exactly on top of each other.
 * - Two edges in opposite directions (A references B and B references A) are mirror images of each other,
 *   point-symmetric about the pair's midpoint. Both graphs draw every node of a kind at one width, so the
 *   two curves cross exactly at that midpoint - which is also where each curve's centre is.
 *
 * Sliding each chip along its own curve does not work for the second case: under a left-to-right layout
 * the returning edge loops back across both nodes, so any point on it away from its centre lies on top of
 * a node. The one place both curves are guaranteed to be clear of the nodes is the gap between them, which
 * is where Dagre reserves label space. So the chips of a group stay at the group's shared centre and are
 * stacked there vertically - chips are wide and short, so a vertical stack is the compact one in either
 * layout direction. An edge alone between its pair of nodes gets no slot and stays centred on its curve,
 * exactly as before.
 *
 * Pure and dependency-free so it runs under Node's test runner without a bundler.
 */

/** The space between two stacked chips, in pixels. */
export const EDGE_LABEL_STACK_GAP = 4;

export interface EdgeLabelStackInput {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  /**
   * The chip's estimated height. Zero means the edge draws no chip, so it takes no place in the stack -
   * hiding labels leaves every edge unslotted and centred.
   */
  readonly height: number;
}

/**
 * Where one chip of a stack sits relative to the group's anchor point. `above` hangs the chip's bottom
 * edge `offset` pixels above the anchor, `below` stands its top edge `offset` pixels below it, and
 * `centre` - only ever the middle chip of an odd stack - centres it on the anchor. The chip's own height
 * is left to CSS (`translateY(-100%)`), so only the chips between it and the anchor are estimates.
 */
export interface EdgeLabelSlot {
  readonly align: "above" | "centre" | "below";
  readonly offset: number;
}

const pairKey = (source: string, target: string) =>
  source <= target ? `${source}\u0000${target}` : `${target}\u0000${source}`;

const sumHeights = (heights: readonly number[], from: number, to: number) => {
  let total = 0;

  for (let index = from; index < to; index++) {
    total += heights[index] + EDGE_LABEL_STACK_GAP;
  }

  return total;
};

/**
 * Assigns a stack slot to every labelled edge that shares its pair of nodes with another labelled edge,
 * in either direction. Within a stack the edges running one way come first and the edges running the
 * other way after them, each in the order given, so the same graph always stacks the same way.
 */
export const stackEdgeLabels = (
  edges: readonly EdgeLabelStackInput[],
): ReadonlyMap<string, EdgeLabelSlot> => {
  const groups = new Map<string, EdgeLabelStackInput[]>();

  for (const edge of edges) {
    if (edge.height <= 0) {
      continue;
    }

    const key = pairKey(edge.source, edge.target);
    const group = groups.get(key);

    if (group) {
      group.push(edge);
    } else {
      groups.set(key, [edge]);
    }
  }

  const slots = new Map<string, EdgeLabelSlot>();

  for (const group of groups.values()) {
    if (group.length < 2) {
      continue;
    }

    const ordered = [
      ...group.filter((edge) => edge.source <= edge.target),
      ...group.filter((edge) => edge.source > edge.target),
    ];
    const heights = ordered.map((edge) => edge.height);
    const count = ordered.length;

    if (count % 2 === 1) {
      // The middle chip sits on the anchor; the rest clear half of it plus a gap, and every chip between.
      const middle = (count - 1) / 2;
      const clearance = heights[middle] / 2 + EDGE_LABEL_STACK_GAP;

      ordered.forEach((edge, index) => {
        slots.set(
          edge.id,
          index === middle
            ? { align: "centre", offset: 0 }
            : index < middle
              ? {
                  align: "above",
                  offset: clearance + sumHeights(heights, index + 1, middle),
                }
              : {
                  align: "below",
                  offset: clearance + sumHeights(heights, middle + 1, index),
                },
        );
      });
    } else {
      // The anchor falls in the gap between the two middle chips.
      const split = count / 2;
      const clearance = EDGE_LABEL_STACK_GAP / 2;

      ordered.forEach((edge, index) => {
        slots.set(
          edge.id,
          index < split
            ? {
                align: "above",
                offset: clearance + sumHeights(heights, index + 1, split),
              }
            : {
                align: "below",
                offset: clearance + sumHeights(heights, split, index),
              },
        );
      });
    }
  }

  return slots;
};

/** A node's box on the canvas, in flow coordinates. */
export interface EdgeLabelNodeBox {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/**
 * The point a stack is centred on: halfway between the centres of the two nodes. Every edge in a group
 * computes it from the same two nodes, so the whole stack lines up whichever edge draws which chip, and it
 * follows the nodes when either is dragged. With nodes of equal size it is exactly the centre of every
 * curve in the group; when a node is taller than the other (the ribbon on the current node) the curves'
 * own centres drift apart by a fraction of the difference, and anchoring each chip to its own curve would
 * let the stack overlap. Returns `undefined` until both nodes have been measured.
 */
export const edgeLabelStackAnchor = (
  source: EdgeLabelNodeBox | undefined,
  target: EdgeLabelNodeBox | undefined,
) => {
  if (!source || !target) {
    return undefined;
  }

  return {
    x: (source.x + source.width / 2 + target.x + target.width / 2) / 2,
    y: (source.y + source.height / 2 + target.y + target.height / 2) / 2,
  };
};

/**
 * The CSS transform that puts a chip at `x`, `y` - centred on it with no slot, or in its place in a stack
 * anchored there.
 */
export const edgeLabelTransform = (
  x: number,
  y: number,
  slot?: EdgeLabelSlot,
) => {
  switch (slot?.align) {
    case "above":
      return `translate(-50%, -100%) translate(${x}px, ${y - slot.offset}px)`;
    case "below":
      return `translate(-50%, 0%) translate(${x}px, ${y + slot.offset}px)`;
    default:
      return `translate(-50%, -50%) translate(${x}px, ${y}px)`;
  }
};
