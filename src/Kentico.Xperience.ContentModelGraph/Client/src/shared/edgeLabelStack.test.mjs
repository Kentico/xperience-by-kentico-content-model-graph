// Run with `npm test` (Node's own test runner strips the types off the module under test).
//
// An edge from a node to itself is drawn as a loop around the node. These cover how several loops on one
// node are nested and their chips stacked, and that each loop keeps off its node.

import test from "node:test";
import assert from "node:assert/strict";

import {
  EDGE_LABEL_STACK_GAP as GAP,
  SELF_LOOP_LABEL_GAP,
  SELF_LOOP_REACH,
  SELF_LOOP_SPACING,
  selfLoopGeometry,
  stackSelfLoops,
} from "./edgeLabelStack.ts";

const edge = (id, source, target, height = 22) => ({
  id,
  source,
  target,
  height,
});

test("self-loops on one node are nested and their chips stacked outwards", () => {
  const slots = stackSelfLoops([
    edge("ab", "a", "b"),
    edge("aa", "a", "a", 22),
    edge("bb", "b", "b", 38),
    edge("aa2", "a", "a", 38),
  ]);

  assert.equal(slots.size, 3);
  assert.deepEqual(slots.get("aa"), {
    index: 0,
    offset: 0,
    height: 22,
    stackHeight: 22 + GAP + 38,
  });
  assert.deepEqual(slots.get("aa2"), {
    index: 1,
    offset: 22 + GAP,
    height: 38,
    stackHeight: 22 + GAP + 38,
  });
  assert.deepEqual(slots.get("bb"), {
    index: 0,
    offset: 0,
    height: 38,
    stackHeight: 38,
  });
});

test("unlabelled self-loops still nest but take no room in the stack", () => {
  const slots = stackSelfLoops([
    edge("aa", "a", "a", 0),
    edge("aa2", "a", "a", 0),
  ]);

  assert.deepEqual(slots.get("aa2"), {
    index: 1,
    offset: 0,
    height: 0,
    stackHeight: 0,
  });
});

// A node at (100, 50), 200 wide and 60 tall. Under LR its source handle is the middle of its right side and
// its target handle the middle of its left side; under TB they are the middle of its bottom and top.
const box = { x: 100, y: 50, width: 200, height: 60 };

// Every coordinate a path visits, control points included - enough to show the loop keeps off the node.
const pathPoints = (path) =>
  [...path.matchAll(/(-?[\d.]+),(-?[\d.]+)/g)].map(([, x, y]) => ({
    x: Number(x),
    y: Number(y),
  }));

const strictlyInside = ({ x, y }) =>
  x > box.x && x < box.x + box.width && y > box.y && y < box.y + box.height;

test("an LR self-loop runs from the right handle under the node into the left handle", () => {
  const { path, labelTransform } = selfLoopGeometry({
    box,
    horizontal: true,
    sourceX: 300,
    sourceY: 80,
    targetX: 100,
    targetY: 80,
  });
  const points = pathPoints(path);

  assert.ok(path.startsWith("M 300,80 "));
  assert.ok(path.endsWith(" 100,80"));
  assert.ok(points.every((point) => !strictlyInside(point)));
  assert.equal(
    Math.max(...points.map((point) => point.y)),
    110 + SELF_LOOP_REACH,
  );
  // The chip hangs below the node, centred on it.
  assert.equal(
    labelTransform,
    `translate(-50%, 0%) translate(200px, ${110 + SELF_LOOP_LABEL_GAP}px)`,
  );
});

test("an LR self-loop runs through the middle of its own chip", () => {
  const { path, labelTransform } = selfLoopGeometry({
    box,
    horizontal: true,
    sourceX: 300,
    sourceY: 80,
    targetX: 100,
    targetY: 80,
    slot: { index: 1, offset: 26, height: 22, stackHeight: 48 },
  });
  const chipTop = 110 + SELF_LOOP_LABEL_GAP + 26;

  assert.equal(
    labelTransform,
    `translate(-50%, 0%) translate(200px, ${chipTop}px)`,
  );
  assert.ok(path.includes(`L 100,${chipTop + 11}`));
});

test("a TB self-loop runs from the bottom handle up the right of the node into the top handle", () => {
  const { path, labelTransform } = selfLoopGeometry({
    box,
    horizontal: false,
    sourceX: 200,
    sourceY: 110,
    targetX: 200,
    targetY: 50,
    slot: { index: 0, offset: 0, height: 22, stackHeight: 22 },
  });
  const points = pathPoints(path);
  const far = 300 + SELF_LOOP_REACH;

  assert.ok(path.startsWith("M 200,110 "));
  assert.ok(path.endsWith(" 200,50"));
  assert.ok(points.every((point) => !strictlyInside(point)));
  assert.ok(path.includes(`L ${far},50`));
  // The chip stands to the right of the node, level with its centre.
  assert.equal(
    labelTransform,
    `translate(0%, 0%) translate(${300 + SELF_LOOP_LABEL_GAP}px, ${80 - 11}px)`,
  );
});

test("further self-loops on one node are drawn further out", () => {
  const far = (index) =>
    Math.max(
      ...pathPoints(
        selfLoopGeometry({
          box,
          horizontal: false,
          sourceX: 200,
          sourceY: 110,
          targetX: 200,
          targetY: 50,
          slot: { index, offset: 0, height: 0, stackHeight: 0 },
        }).path,
      ).map((point) => point.x),
    );

  assert.equal(far(1) - far(0), SELF_LOOP_SPACING);
});
