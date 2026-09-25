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

// One reference drawn on an edge. An edge can carry several: a page that points at the same item from its
// page template and from a widget produces two references between the same pair of nodes, and two edges
// with identical endpoints are drawn as one curve with their chips exactly on top of each other. They are
// merged into a single edge instead, whose chip names every reference it stands for.
export interface RelationshipEdgeLabelEntry {
  readonly label: string;
  // Where in the Page Builder configuration the reference lives, one line per occurrence. Shown as part of
  // the label's tooltip, because a reference reachable only from a personalization variant is invisible to
  // an editor until that variant is selected.
  readonly path?: string | null;
}

export interface RelationshipEdgeData extends Record<string, unknown> {
  readonly entries: readonly RelationshipEdgeLabelEntry[];
  readonly broken: boolean;
}

export type RelationshipEdge = Edge<RelationshipEdgeData, "relationshipEdge">;

// A merged chip states every reference it stands for, but a page pointing at the same item from a dozen
// widgets would grow a chip taller than the nodes on either side of it. Only this many entries are drawn;
// the rest are counted off in an overflow line and spelled out in full in the tooltip, so a merged chip
// never hides a reference silently.
const RELATIONSHIP_EDGE_LABEL_MAX_ENTRIES = 3;

// The divider drawn between two merged entries: its border plus the space either side of it. Kept in sync
// with the `margin-top`, `padding-top` and `border-top` of `.cmg-relationships-edge-label__entry`.
const EDGE_LABEL_ENTRY_SEPARATOR_HEIGHT = 7;

// The server emits the fully qualified Page Builder component identifier, for example
// `Widget: DancingGoat.LandingPage.HeroImage · variant "Coffee sale"`. Every widget on a page repeats the
// same namespace prefix, which makes the edge label several times wider than the gap between two ranks, so
// only the last segment of the type identifier is drawn, and the variant moves onto a line of its own:
//
//   Widget: HeroImage
//   Variant: "Coffee sale"
//
// The source kind stays on the label - a reference from a section or a page template must not read as a
// widget - and the full value stays reachable from the label's tooltip. A reference found on several
// variants at once, which the server reports as `· personalized` because it has no single name, keeps the
// same shape: `Variant: personalized`, unquoted, so it cannot be mistaken for a variant actually called
// that.
export const shortenRelationshipEdgeLabel = (label: string) => {
  // Only dotted type identifiers emitted for Page Builder sources are shortened. Plain field labels
  // ("Related articles") never match and are left exactly as they are.
  const match = /^((?:Widget|Section|Template): )(\S+)(.*)$/.exec(label);

  if (!match) {
    return label;
  }

  const [, sourceKind, typeIdentifier, suffix] = match;

  return `${sourceKind}${typeIdentifier.slice(
    typeIdentifier.lastIndexOf(".") + 1,
  )}${suffix.replace(/^ · (?:variant )?/, "\nVariant: ")}`;
};

// An entry with no label would draw an empty block under a divider, so unnamed entries are dropped before
// anything is measured or rendered.
const namedEntries = (entries: readonly RelationshipEdgeLabelEntry[]) =>
  entries.filter((entry) => Boolean(entry.label));

// What the chip actually draws: one shortened block per entry it can show, plus the overflow line for the
// ones it cannot. Layout and the component both go through this, so the space Dagre reserves is the space
// the chip needs. A single-entry edge - by far the common case - comes back as one block and no overflow,
// which renders exactly as an unmerged label always has.
export const relationshipEdgeLabelBlocks = (
  entries: readonly RelationshipEdgeLabelEntry[],
) => {
  const named = namedEntries(entries);
  const hiddenCount = Math.max(
    named.length - RELATIONSHIP_EDGE_LABEL_MAX_ENTRIES,
    0,
  );

  return {
    blocks: named
      .slice(0, named.length - hiddenCount)
      .map((entry) => shortenRelationshipEdgeLabel(entry.label)),
    overflow: hiddenCount > 0 ? `+${hiddenCount} more` : null,
  };
};

// Everything the chip could not say itself. A shown entry contributes its Page Builder path, or - with no
// path - the fully qualified identifier it was shortened from, so nothing the server sent becomes
// unrecoverable. An entry the chip had no room for contributes its label as well, because it is not on the
// chip at all. Lines are deduplicated: merged references frequently sit at the same Page Builder path.
const relationshipEdgeLabelTooltip = (
  entries: readonly RelationshipEdgeLabelEntry[],
) => {
  const lines = namedEntries(entries).flatMap((entry, index) => {
    const path = entry.path?.trim();
    const pathLines = path ? path.split("\n") : [];

    if (index >= RELATIONSHIP_EDGE_LABEL_MAX_ENTRIES) {
      return [entry.label, ...pathLines];
    }

    if (pathLines.length > 0) {
      return pathLines;
    }

    return shortenRelationshipEdgeLabel(entry.label) === entry.label
      ? []
      : [entry.label];
  });

  return [...new Set(lines)].join("\n") || undefined;
};

// Dagre reserves no room for edge labels unless it is told how big they are, so every edge passes the
// rendered chip's estimated box through to the layout. A forced line break has to be measured as one -
// sizing a whole string as a single run would under-report the height of a two-segment label and put the
// chip back on top of the nodes - so each segment is wrapped on its own and the segments are summed, across
// every block the chip draws, with the dividers between them added on. The width is the widest segment
// rather than the whole string, for the same reason.
export const estimateRelationshipEdgeLabelSize = (
  entries: readonly RelationshipEdgeLabelEntry[],
) => {
  const { blocks, overflow } = relationshipEdgeLabelBlocks(entries);
  const drawn = overflow === null ? blocks : [...blocks, overflow];

  if (drawn.length === 0) {
    return { width: 0, height: 0 };
  }

  let width = 0;
  let lines = 0;

  for (const segment of drawn.flatMap((block) => block.split("\n"))) {
    const estimate = estimateEdgeLabelSegment(segment);

    width = Math.max(width, estimate.width);
    lines += estimate.lines;
  }

  return {
    width,
    height:
      lines * EDGE_LABEL_LINE_HEIGHT +
      EDGE_LABEL_VERTICAL_CHROME +
      (drawn.length - 1) * EDGE_LABEL_ENTRY_SEPARATOR_HEIGHT,
  };
};

// The label is rendered as HTML rather than through ReactFlow's built-in SVG edge label so it can carry a
// tooltip - an SVG <text> label has nowhere to hang one - and so a merged chip can rule its entries off
// from one another, which a single run of text cannot do.
export const RelationshipEdgeComponent = ({
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
}: EdgeProps<RelationshipEdge>) => {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  const entries = data?.entries ?? [];
  const { blocks, overflow } = relationshipEdgeLabelBlocks(entries);
  const title = relationshipEdgeLabelTooltip(entries);

  return (
    <>
      <BaseEdge id={id} path={edgePath} markerEnd={markerEnd} style={style} />
      {blocks.length > 0 && (
        <EdgeLabelRenderer>
          <div
            className={`cmg-edge-label cmg-relationships-edge-label nodrag nopan${
              data?.broken ? " cmg-relationships-edge-label--missing" : ""
            }`}
            style={{
              transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
            }}
            title={title}
          >
            {blocks.map((block, index) => (
              <div
                // Blocks come from the edge's own entries in a fixed order, so the index is stable for as
                // long as the edge is.
                key={`${index}:${block}`}
                className="cmg-edge-label__line cmg-relationships-edge-label__entry"
              >
                {block}
              </div>
            ))}
            {overflow !== null && (
              <div className="cmg-edge-label__line cmg-relationships-edge-label__entry cmg-relationships-edge-label__overflow">
                {overflow}
              </div>
            )}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  );
};
