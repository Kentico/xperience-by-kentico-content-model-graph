// Run with `npm test` (Node's own test runner strips the types off the module under test).
//
// An edge runs between the sides of its two nodes that face each other, and edges sharing a pair of nodes
// are bowed into lanes with each chip on its own curve. An edge alone between its nodes with its target
// ahead of it must stay exactly the curve ReactFlow draws.

import test from "node:test";
import assert from "node:assert/strict";

import { getBezierPath } from "@xyflow/system";

import {
  EDGE_LANE_GAP,
  EDGE_LANE_MIN_WIDTH,
  assignEdgeLanes,
  edgeRoute,
  laneOffsets,
} from "./edgeRoute.ts";

const edge = (id, source, target, width = 100, height = 22) => ({
  id,
  source,
  target,
  width,
  height,
});

// Two nodes 200 wide and 60 tall, side by side 200 apart. Under LR a node's source handle is the middle of
// its right side and its target handle the middle of its left side.
const left = { x: 0, y: 0, width: 200, height: 60 };
const right = { x: 400, y: 0, width: 200, height: 60 };

// ReactFlow always reports the source's right handle and the target's left handle under LR.
const lr = (sourceBox, targetBox, extra = {}) => ({
  sourceBox,
  targetBox,
  horizontal: true,
  sourceX: sourceBox.x + sourceBox.width,
  sourceY: sourceBox.y + sourceBox.height / 2,
  targetX: targetBox.x,
  targetY: targetBox.y + targetBox.height / 2,
  ...extra,
});

const endpoints = (path) => {
  const points = [...path.matchAll(/(-?[\d.]+),(-?[\d.]+)/g)].map(
    ([, x, y]) => ({ x: Number(x), y: Number(y) }),
  );

  return { start: points[0], end: points[points.length - 1] };
};

// --- lanes ------------------------------------------------------------------

test("an edge alone between its nodes gets no lane, nor does a self-loop", () => {
  const lanes = assignEdgeLanes([
    edge("ab", "a", "b"),
    edge("aa", "a", "a"),
    edge("aa2", "a", "a"),
  ]);

  assert.equal(lanes.size, 0);
});

test("reciprocal edges share a group, lower-to-higher first", () => {
  const lanes = assignEdgeLanes([
    edge("ba", "b", "a", 50, 40),
    edge("ab", "a", "b", 100, 22),
  ]);
  const sizes = [
    { width: 100, height: 22 },
    { width: 50, height: 40 },
  ];

  assert.deepEqual(lanes.get("ab"), { index: 0, sizes });
  assert.deepEqual(lanes.get("ba"), { index: 1, sizes });
});

test("parallel edges in one direction take lanes in input order", () => {
  const lanes = assignEdgeLanes([edge("ab1", "a", "b"), edge("ab2", "a", "b")]);

  assert.equal(lanes.get("ab1")?.index, 0);
  assert.equal(lanes.get("ab2")?.index, 1);
});

test("unlabelled edges sharing a pair still get lanes", () => {
  const lanes = assignEdgeLanes([
    edge("ab", "a", "b", 0, 0),
    edge("ba", "b", "a", 0, 0),
  ]);

  assert.equal(lanes.size, 2);
});

test("pairs are grouped independently", () => {
  const lanes = assignEdgeLanes([
    edge("ab", "a", "b"),
    edge("ba", "b", "a"),
    edge("ac", "a", "c"),
    edge("ca", "c", "a"),
    edge("bc", "b", "c"),
  ]);

  assert.equal(lanes.get("ab")?.sizes.length, 2);
  assert.equal(lanes.get("ac")?.sizes.length, 2);
  assert.equal(lanes.get("bc"), undefined);
});

test("lanes are centred on the line and as wide as their chips reach across it", () => {
  const sizes = [
    { width: 100, height: 30 },
    { width: 100, height: 40 },
  ];
  const down = laneOffsets(sizes, { x: 0, y: 1 });

  // 30 + gap + 40 in all, centred.
  const total = 30 + EDGE_LANE_GAP + 40;
  assert.deepEqual(down, [-total / 2 + 15, total / 2 - 20]);

  // Across a vertical line the widths are what separate the chips.
  const across = laneOffsets(sizes, { x: 1, y: 0 });
  assert.deepEqual(across, [
    -(100 + EDGE_LANE_GAP) / 2,
    (100 + EDGE_LANE_GAP) / 2,
  ]);
});

test("an unlabelled lane still takes the minimum width", () => {
  const offsets = laneOffsets(
    [
      { width: 0, height: 0 },
      { width: 0, height: 0 },
    ],
    { x: 0, y: 1 },
  );

  assert.equal(offsets[1] - offsets[0], EDGE_LANE_MIN_WIDTH + EDGE_LANE_GAP);
});

// --- routes -----------------------------------------------------------------

test("a lone forward edge is exactly ReactFlow's bezier", () => {
  const target = { ...right, y: 50 };
  const input = lr(left, target);
  const route = edgeRoute(input);
  const [path, labelX, labelY] = getBezierPath({
    sourceX: input.sourceX,
    sourceY: input.sourceY,
    sourcePosition: "right",
    targetX: input.targetX,
    targetY: input.targetY,
    targetPosition: "left",
  });

  assert.equal(route.path, path);
  assert.equal(route.labelX, labelX);
  assert.equal(route.labelY, labelY);
  assert.equal(
    route.labelTransform,
    `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
  );
});

test("a backward LR edge leaves its source's left side and enters its target's right side", () => {
  const route = edgeRoute(lr(right, left));
  const { start, end } = endpoints(route.path);

  assert.deepEqual(start, { x: 400, y: 30 });
  assert.deepEqual(end, { x: 200, y: 30 });
  assert.equal(route.labelX, 300);
  // It stays in the gap rather than doubling back behind either node.
  const xs = [...route.path.matchAll(/(-?[\d.]+),/g)].map(([, x]) => Number(x));
  assert.ok(xs.every((x) => x >= 200 && x <= 400));
});

test("a backward TB edge leaves its source's top and enters its target's bottom", () => {
  const top = { x: 0, y: 0, width: 200, height: 60 };
  const bottom = { x: 0, y: 300, width: 200, height: 60 };
  const route = edgeRoute({
    sourceBox: bottom,
    targetBox: top,
    horizontal: false,
    sourceX: 100,
    sourceY: 360,
    targetX: 100,
    targetY: 0,
  });
  const { start, end } = endpoints(route.path);

  assert.deepEqual(start, { x: 100, y: 300 });
  assert.deepEqual(end, { x: 100, y: 60 });
  assert.equal(route.labelY, 180);
});

test("reciprocal edges run between the facing sides, bowed apart, each chip on its own curve", () => {
  const lanes = assignEdgeLanes([
    edge("ab", "a", "b", 100, 22),
    edge("ba", "b", "a", 100, 22),
  ]);
  const ab = edgeRoute(lr(left, right, { lane: lanes.get("ab") }));
  const ba = edgeRoute(
    lr(right, left, { lane: lanes.get("ba"), reversed: true }),
  );

  // Both between the right side of `a` and the left side of `b`, in opposite directions.
  assert.deepEqual(endpoints(ab.path), {
    start: { x: 200, y: 30 },
    end: { x: 400, y: 30 },
  });
  assert.deepEqual(endpoints(ba.path), {
    start: { x: 400, y: 30 },
    end: { x: 200, y: 30 },
  });

  // Symmetric about the line between the nodes, far enough apart that the chips cannot touch.
  assert.ok(Math.abs(ab.labelX - 300) < 1e-9);
  assert.ok(Math.abs(ba.labelX - 300) < 1e-9);
  assert.ok(Math.abs(ab.labelY - 30 + (ba.labelY - 30)) < 1e-9);
  assert.ok(Math.abs(ab.labelY - ba.labelY) >= 22 + EDGE_LANE_GAP - 1e-9);
  assert.notEqual(ab.path, ba.path);
});

test("parallel edges in one direction are bowed apart too", () => {
  const lanes = assignEdgeLanes([
    edge("ab1", "a", "b", 100, 22),
    edge("ab2", "a", "b", 100, 22),
  ]);
  const first = edgeRoute(lr(left, right, { lane: lanes.get("ab1") }));
  const second = edgeRoute(lr(left, right, { lane: lanes.get("ab2") }));

  assert.ok(
    Math.abs(first.labelY - second.labelY) >= 22 + EDGE_LANE_GAP - 1e-9,
  );
});

test("lanes follow a dragged node and keep their chips apart at an angle", () => {
  const lanes = assignEdgeLanes([
    edge("ab", "a", "b", 120, 40),
    edge("ba", "b", "a", 120, 40),
  ]);
  const dragged = { ...right, y: 250 };
  const ab = edgeRoute(lr(left, dragged, { lane: lanes.get("ab") }));
  const ba = edgeRoute(
    lr(dragged, left, { lane: lanes.get("ba"), reversed: true }),
  );

  // Two 120x40 chips centred on these points do not overlap.
  const apartX = Math.abs(ab.labelX - ba.labelX) >= 120;
  const apartY = Math.abs(ab.labelY - ba.labelY) >= 40;
  assert.ok(apartX || apartY);
});
