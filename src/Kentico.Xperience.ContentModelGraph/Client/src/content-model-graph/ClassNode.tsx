import { createContext, useContext } from "react";
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";

import { nodeColor, type NodeKind } from "./model";

export interface ClassNodeData extends Record<string, unknown> {
  readonly displayName: string;
  readonly name: string;
  readonly adminUrl?: string;
  readonly kind: NodeKind;
  readonly fieldCount: number;
  readonly horizontal: boolean;
}

export type ClassNode = Node<ClassNodeData, "classNode">;

export const CLASS_NODE_WIDTH = 230;
export const CLASS_NODE_HEIGHT = 56;
export const ClassNodeSearchContext = createContext("");

export const ClassNodeComponent = ({ data }: NodeProps<ClassNode>) => {
  const search = useContext(ClassNodeSearchContext);
  const dimmed =
    search.length > 0 &&
    !`${data.displayName} ${data.name}`.toLowerCase().includes(search);
  const details =
    data.kind === "taxonomy"
      ? data.name
      : `${data.name} | ${data.fieldCount} fields`;

  return (
    <div
      className={`cmg-node${dimmed ? " cmg-node--dimmed" : ""}`}
      style={{ borderLeftColor: nodeColor(data.kind) }}
      title={details}
    >
      <Handle
        type="target"
        position={data.horizontal ? Position.Left : Position.Top}
      />
      {data.adminUrl ? (
        <a className="cmg-node__title nodrag" href={data.adminUrl}>
          {data.displayName}
        </a>
      ) : (
        <div className="cmg-node__title">{data.displayName}</div>
      )}
      <div className="cmg-node__meta">{details}</div>
      <Handle
        type="source"
        position={data.horizontal ? Position.Right : Position.Bottom}
      />
    </div>
  );
};
