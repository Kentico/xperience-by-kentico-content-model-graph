/**
 * Routes an edge between two different nodes.
 *
 * Every node has a target handle on its leading side (left under `LR`, top under `TB`) and a source handle on
 * its trailing side (right, bottom). ReactFlow always draws an edge from the source's trailing handle to the
 * target's leading one, so an edge whose target lies behind its source - a reference back to the root in the
 * relationships graph, or the returning half of two classes referencing each other - doubles back across
 * both nodes. Here each edge instead runs between the sides of its two nodes that face each other, decided
 * from where the nodes are right now, so it follows a drag: a backward edge leaves its source's leading side
 * and enters its target's trailing side. Both sides already carry a handle, so no extra handles are needed.
 *
 * Edges sharing a pair of nodes - two nodes referencing each other, or two fields of one class pointing at
 * the same class - would then be drawn on the same line. Each of them gets a lane instead: the curves are
 * bowed apart, symmetric about the line between the two nodes, and each chip sits on its own curve's
 * midpoint, far enough from its neighbours that the chips never overlap.
 *
 * An edge alone between its nodes with its target ahead of it is drawn exactly as ReactFlow's own bezier
 * edge would draw it.
 *
 * Pure and dependency-free so it runs under Node's test runner without a bundler.
 */

import type { EdgeLabelNodeBox } from "./edgeLabelStack";

/** The least room a lane takes across the line between its nodes, so unlabelled parallel edges stay apart. */
export const EDGE_LANE_MIN_WIDTH = 24;

/** The space between the chips of neighbouring lanes. */
export const EDGE_LANE_GAP = 8;

// ReactFlow's bezier curvature, so an unbowed edge matches `getBezierPath` exactly.
const CURVATURE = 0.25;

// A cubic bezier's midpoint moves by three quarters of what both control points are moved by.
const MIDPOINT_TO_CONTROL = 4 / 3;

export interface EdgeLaneInput {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  /** The chip's estimated size; zero when the edge draws no chip. */
  readonly width: number;
  readonly height: number;
}

/**
 * One edge's place among the edges sharing its pair of nodes. `sizes` are the chips of the whole group in
 * lane order, so each edge can work out every lane's width from the live node positions on its own.
 */
export interface EdgeLane {
  readonly index: number;
  readonly sizes: readonly {
    readonly width: number;
    readonly height: number;
  }[];
}

const pairKey = (source: string, target: string) =>
  source <= target ? `${source}\u0000${target}` : `${target}\u0000${source}`;

/**
 * Assigns a lane to every edge that shares its unordered pair of nodes with another edge. Within a group the
 * edges running from the lower node id to the higher come first, then the others, each in the order given,
 * so the same graph always lays out the same way. Self-loops and edges alone between their nodes get none.
 */
export const assignEdgeLanes = (
  edges: readonly EdgeLaneInput[],
): ReadonlyMap<string, EdgeLane> => {
  const groups = new Map<string, EdgeLaneInput[]>();

  for (const edge of edges) {
    if (edge.source === edge.target) {
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

  const lanes = new Map<string, EdgeLane>();

  for (const group of groups.values()) {
    if (group.length < 2) {
      continue;
    }

    const ordered = [
      ...group.filter((edge) => edge.source < edge.target),
      ...group.filter((edge) => edge.source > edge.target),
    ];
    const sizes = ordered.map(({ width, height }) => ({
      width: Math.max(width, 0),
      height: Math.max(height, 0),
    }));

    ordered.forEach((edge, index) => lanes.set(edge.id, { index, sizes }));
  }

  return lanes;
};

type Side = "left" | "right" | "top" | "bottom";

interface Point {
  readonly x: number;
  readonly y: number;
}

const centreOf = (box: EdgeLabelNodeBox): Point => ({
  x: box.x + box.width / 2,
  y: box.y + box.height / 2,
});

// ReactFlow's `calculateControlOffset`: half the distance ahead of the handle, or a short swing back out of
// it when the other end lies behind.
const controlOffset = (distance: number) =>
  distance >= 0 ? 0.5 * distance : CURVATURE * 25 * Math.sqrt(-distance);

const controlPoint = (side: Side, from: Point, to: Point): Point => {
  switch (side) {
    case "left":
      return { x: from.x - controlOffset(from.x - to.x), y: from.y };
    case "right":
      return { x: from.x + controlOffset(to.x - from.x), y: from.y };
    case "top":
      return { x: from.x, y: from.y - controlOffset(from.y - to.y) };
    default:
      return { x: from.x, y: from.y + controlOffset(to.y - from.y) };
  }
};

/**
 * How far along the lane normal each lane's midpoint sits from the line between the nodes. The lanes are
 * packed side by side and centred on that line; each is as wide as its chip reaches along the normal, so
 * neighbouring chips are separated along it and cannot overlap whatever angle the line is at.
 */
export const laneOffsets = (
  sizes: readonly { readonly width: number; readonly height: number }[],
  normal: Point,
) => {
  const extents = sizes.map((size) =>
    Math.max(
      EDGE_LANE_MIN_WIDTH,
      Math.abs(normal.x) * size.width + Math.abs(normal.y) * size.height,
    ),
  );
  const total =
    extents.reduce((sum, extent) => sum + extent, 0) +
    EDGE_LANE_GAP * Math.max(extents.length - 1, 0);
  const offsets: number[] = [];
  let lead = -total / 2;

  for (const extent of extents) {
    offsets.push(lead + extent / 2);
    lead += extent + EDGE_LANE_GAP;
  }

  return offsets;
};

export interface EdgeRouteInput {
  readonly sourceBox: EdgeLabelNodeBox;
  readonly targetBox: EdgeLabelNodeBox;
  /** `LR` layout: handles on the left and right. Otherwise `TB`: handles on the top and bottom. */
  readonly horizontal: boolean;
  /** The source handle ReactFlow reports: on the source's trailing side (right, or bottom). */
  readonly sourceX: number;
  readonly sourceY: number;
  /** The target handle ReactFlow reports: on the target's leading side (left, or top). */
  readonly targetX: number;
  readonly targetY: number;
  /** Absent for an edge alone between its nodes. */
  readonly lane?: EdgeLane;
  /**
   * The edge runs from the higher node id to the lower. Lanes are measured from the lower node's side of
   * the pair, so both directions agree on which side of the line each lane lies.
   */
  readonly reversed?: boolean;
}

/**
 * The edge's SVG path, the point its chip is centred on, and the CSS transform that puts it there.
 */
export const edgeRoute = ({
  sourceBox,
  targetBox,
  horizontal,
  sourceX,
  sourceY,
  targetX,
  targetY,
  lane,
  reversed = false,
}: EdgeRouteInput) => {
  const sourceCentre = centreOf(sourceBox);
  const targetCentre = centreOf(targetBox);
  // The target lies behind the source when its centre is further left (`LR`) or further up (`TB`).
  const backward = horizontal
    ? targetCentre.x < sourceCentre.x
    : targetCentre.y < sourceCentre.y;

  // A backward edge uses the handles on the other side of each node - the reported handle mirrored through
  // its node's centre - so it leaves from the side facing the target and enters the side facing the source.
  const mirror = (point: Point, centre: Point): Point =>
    horizontal
      ? { x: 2 * centre.x - point.x, y: point.y }
      : { x: point.x, y: 2 * centre.y - point.y };
  const reportedSource = { x: sourceX, y: sourceY };
  const reportedTarget = { x: targetX, y: targetY };
  const start = backward
    ? mirror(reportedSource, sourceCentre)
    : reportedSource;
  const end = backward ? mirror(reportedTarget, targetCentre) : reportedTarget;
  const sourceSide: Side = horizontal
    ? backward
      ? "left"
      : "right"
    : backward
      ? "top"
      : "bottom";
  const targetSide: Side = horizontal
    ? backward
      ? "right"
      : "left"
    : backward
      ? "bottom"
      : "top";

  let control1 = controlPoint(sourceSide, start, end);
  let control2 = controlPoint(targetSide, end, start);

  if (lane) {
    // The lane normal is the line from the lower node id to the higher, turned a quarter clockwise.
    const from = reversed ? targetCentre : sourceCentre;
    const to = reversed ? sourceCentre : targetCentre;
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const length = Math.hypot(dx, dy);
    const normal =
      length > 0
        ? { x: -dy / length, y: dx / length }
        : horizontal
          ? { x: 0, y: 1 }
          : { x: 1, y: 0 };
    const shift =
      (laneOffsets(lane.sizes, normal)[lane.index] ?? 0) * MIDPOINT_TO_CONTROL;

    control1 = {
      x: control1.x + normal.x * shift,
      y: control1.y + normal.y * shift,
    };
    control2 = {
      x: control2.x + normal.x * shift,
      y: control2.y + normal.y * shift,
    };
  }

  const labelX =
    start.x * 0.125 + control1.x * 0.375 + control2.x * 0.375 + end.x * 0.125;
  const labelY =
    start.y * 0.125 + control1.y * 0.375 + control2.y * 0.375 + end.y * 0.125;

  return {
    path: `M${start.x},${start.y} C${control1.x},${control1.y} ${control2.x},${control2.y} ${end.x},${end.y}`,
    labelX,
    labelY,
    labelTransform: edgeLabelTransform(labelX, labelY),
  };
};

/** The CSS transform that centres a chip on `x`, `y`. */
export const edgeLabelTransform = (x: number, y: number) =>
  `translate(-50%, -50%) translate(${x}px, ${y}px)`;
