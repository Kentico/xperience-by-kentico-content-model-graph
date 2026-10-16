/**
 * Geometry shared by both graphs' edge label chips.
 *
 * Both graphs draw their edge labels as HTML chips rather than through ReactFlow's built-in SVG labels,
 * and Dagre reserves no room for an edge label unless the edge carries its dimensions - so both have to
 * estimate the size of the same box. The numbers live here once, next to each other, because they are all
 * kept in sync with the one stylesheet that paints the chip (`EdgeLabel.css`): a value that drifted in one
 * graph and not the other would put that graph's labels back on top of its nodes.
 */

/**
 * The widest a chip is allowed to render, in pixels. Kept in sync with the `max-width` of
 * `.cmg-edge-label` so the space reserved during layout matches what is painted.
 */
export const EDGE_LABEL_MAX_WIDTH = 200;

/**
 * How many lines one run of text may wrap to. One pathological label therefore cannot stretch the layout
 * vertically. Kept in sync with the `line-clamp` of `.cmg-edge-label__line`.
 */
export const EDGE_LABEL_MAX_LINES_PER_SEGMENT = 2;

/** Rough per-character advance for 12px Inter - an estimate, deliberately not a DOM measurement. */
export const EDGE_LABEL_CHAR_WIDTH = 6.5;

/** The chip's horizontal padding plus its left and right borders. */
export const EDGE_LABEL_HORIZONTAL_CHROME = 14;

/** Kept in sync with the `line-height` of `.cmg-edge-label`. */
export const EDGE_LABEL_LINE_HEIGHT = 16;

/** The chip's vertical padding plus its top and bottom borders. */
export const EDGE_LABEL_VERTICAL_CHROME = 6;

/**
 * The painted size of one run of text inside a chip: how wide it needs to be, and how many lines it wraps
 * to once clamped. Callers sum the lines of every run they draw and add whatever chrome sits between them,
 * because what a chip draws differs per graph - a single field name here, several merged references there.
 */
export const estimateEdgeLabelSegment = (segment: string) => {
  const textWidth =
    segment.length * EDGE_LABEL_CHAR_WIDTH + EDGE_LABEL_HORIZONTAL_CHROME;

  return {
    width: Math.min(textWidth, EDGE_LABEL_MAX_WIDTH),
    lines: Math.min(
      Math.max(Math.ceil(textWidth / EDGE_LABEL_MAX_WIDTH), 1),
      EDGE_LABEL_MAX_LINES_PER_SEGMENT,
    ),
  };
};
