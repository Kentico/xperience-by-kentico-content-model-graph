import { createContext, useContext } from "react";
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";

import { nodeColor, type NodeKind } from "./model";

export interface ClassNodeData extends Record<string, unknown> {
  readonly displayName: string;
  readonly name: string;
  readonly adminUrl?: string;
  readonly kind: NodeKind;
  readonly fieldCount: number;
  readonly schemaFieldCount?: number;
  readonly horizontal: boolean;
  /**
   * The node the contextual pages filtered the graph down to. False on every
   * node of the unfiltered graph, which has no focal node at all.
   */
  readonly isCurrent: boolean;
}

export type ClassNode = Node<ClassNodeData, "classNode">;

export const CLASS_NODE_WIDTH = 230;

/**
 * Must stay in sync with the fixed `height` of `.cmg-node` in
 * ContentModelGraph.css. The Dagre layout reserves exactly this much room per
 * node, so the node is a fixed size: the title may wrap onto a second line and
 * that second line is always reserved, whether or not a given title uses it.
 */
export const CLASS_NODE_HEIGHT = 74;

/**
 * What the "Current item" ribbon adds to the node it marks. Must stay in sync
 * with the height of `.cmg-node__current` and with the extra `height` of
 * `.cmg-node--current` in ContentModelGraph.css.
 */
const CLASS_NODE_CURRENT_EXTRA_HEIGHT = 24;

/**
 * Nodes here are a fixed size, unlike the relationships graph where every node
 * is estimated from its contents - but the one node the contextual pages focus
 * on wears a ribbon the others do not, and the layout has to be told, or that
 * node overlaps its neighbours. So the height is a function of the one thing
 * that varies, rather than a second constant callers have to choose between.
 */
export const classNodeHeight = (isCurrent: boolean) =>
  isCurrent
    ? CLASS_NODE_HEIGHT + CLASS_NODE_CURRENT_EXTRA_HEIGHT
    : CLASS_NODE_HEIGHT;

export const ClassNodeSearchContext = createContext("");

/**
 * `kind` names what the fields are, e.g. "schema" renders "3 schema fields" and
 * "No schema fields". Pass an empty string for a plain "3 fields".
 */
const fieldCountLabel = (fieldCount: number, kind = "") => {
  const noun = kind.length > 0 ? `${kind} field` : "field";

  if (fieldCount <= 0) {
    return `No ${noun}s`;
  }

  return fieldCount === 1 ? `1 ${noun}` : `${fieldCount} ${noun}s`;
};

/**
 * The count lines of a node's tooltip. `fieldCount` only ever covers a node's own
 * fields, so it is labelled "non-schema" wherever a node could also inherit fields
 * from an assigned reusable field schema — which is everywhere except on a schema
 * node itself, whose own fields are simply its fields. Taxonomies have no fields of
 * their own, so they get no count lines. `schemaFieldCount` is undefined when a node
 * has no assigned schemas, and that line is then omitted rather than shown as zero.
 */
const fieldCountLines = (
  kind: NodeKind,
  fieldCount: number,
  schemaFieldCount: number | undefined,
): string[] => {
  if (kind === "taxonomy") {
    return [];
  }

  if (kind === "schema") {
    return [fieldCountLabel(fieldCount)];
  }

  const lines = [fieldCountLabel(fieldCount, "non-schema")];

  if (schemaFieldCount !== undefined) {
    lines.push(fieldCountLabel(schemaFieldCount, "schema"));
  }

  return lines;
};

export const ClassNodeComponent = ({ data }: NodeProps<ClassNode>) => {
  const search = useContext(ClassNodeSearchContext);
  const dimmed =
    search.length > 0 &&
    !`${data.displayName} ${data.name}`.toLowerCase().includes(search);
  // The field counts live only in the tooltip, where they are unambiguously about
  // this one node.
  const tooltip = [
    data.displayName,
    data.name,
    ...fieldCountLines(data.kind, data.fieldCount, data.schemaFieldCount),
  ].join("\n");

  return (
    <div
      className={`cmg-node${data.isCurrent ? " cmg-node--current" : ""}${
        dimmed ? " cmg-node--dimmed" : ""
      }`}
      style={{ borderLeftColor: nodeColor(data.kind) }}
      title={tooltip}
    >
      <Handle
        type="target"
        position={data.horizontal ? Position.Left : Position.Top}
      />
      {data.isCurrent && <div className="cmg-node__current">Current item</div>}
      {data.adminUrl ? (
        <a className="cmg-node__title nodrag" href={data.adminUrl}>
          {data.displayName}
        </a>
      ) : (
        <div className="cmg-node__title">{data.displayName}</div>
      )}
      <div className="cmg-node__meta">{data.name}</div>
      <Handle
        type="source"
        position={data.horizontal ? Position.Right : Position.Bottom}
      />
    </div>
  );
};
