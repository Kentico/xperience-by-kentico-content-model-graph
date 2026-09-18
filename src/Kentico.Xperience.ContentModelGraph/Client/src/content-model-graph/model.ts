export type NodeKind =
  | "website"
  | "reusable"
  | "email"
  | "headless"
  | "schema"
  | "taxonomy"
  | "forms"
  | "objectType"
  | "systemObjectType";

export type SystemObjectTypeGroup =
  | "CMS"
  | "EmailLibrary"
  | "OM"
  | "Commerce"
  | "AIRA"
  | "Other";

export type EdgeKind =
  | "schemaAssignment"
  | "contentReference"
  | "schemaReference"
  | "objectReference"
  | "taxonomyReference";

export interface GraphNodeDto {
  readonly id: string;
  readonly name: string;
  readonly displayName: string;
  readonly adminUrl?: string;
  readonly kind: NodeKind;
  readonly systemObjectTypeGroup?: SystemObjectTypeGroup;
  readonly fieldCount: number;
}

export interface GraphEdgeDto {
  readonly id: string;
  readonly source: string;
  readonly target: string;
  readonly kind: EdgeKind;
  readonly label: string;
}

export interface GraphDataDto {
  readonly nodes: readonly GraphNodeDto[];
  readonly edges: readonly GraphEdgeDto[];
}

export const nodeKinds: ReadonlyArray<{
  kind: NodeKind;
  label: string;
  color: string;
}> = [
  { kind: "website", label: "Page content types", color: "#3d5dff" },
  { kind: "reusable", label: "Reusable content types", color: "#007d72" },
  { kind: "email", label: "Email content types", color: "#9e6200" },
  { kind: "headless", label: "Headless content types", color: "#c64300" },
  { kind: "schema", label: "Reusable field schemas", color: "#7f09b7" },
  { kind: "taxonomy", label: "Taxonomies", color: "#006b5f" },
  { kind: "forms", label: "Forms", color: "#b35c00" },
  { kind: "objectType", label: "Custom object types", color: "#00704a" },
  { kind: "systemObjectType", label: "System object types", color: "#8c8c8c" },
];

export const systemObjectTypeGroups: ReadonlyArray<{
  group: SystemObjectTypeGroup;
  label: string;
}> = [
  { group: "CMS", label: "CMS" },
  { group: "EmailLibrary", label: "Emails" },
  { group: "OM", label: "Marketing" },
  { group: "Commerce", label: "Commerce" },
  { group: "AIRA", label: "AIRA" },
  { group: "Other", label: "Other" },
];

export const edgeKinds: ReadonlyArray<{
  kind: EdgeKind;
  label: string;
  color: string;
  dashed: boolean;
}> = [
  {
    kind: "schemaAssignment",
    label: "Schema assignment",
    color: "#7f09b7",
    dashed: false,
  },
  {
    kind: "contentReference",
    label: "Content type reference",
    color: "#3d5dff",
    dashed: false,
  },
  {
    kind: "schemaReference",
    label: "Schema-filtered reference",
    color: "#5d0088",
    dashed: true,
  },
  {
    kind: "objectReference",
    label: "Object type reference",
    color: "#8c8c8c",
    dashed: true,
  },
  {
    kind: "taxonomyReference",
    label: "Taxonomy reference",
    color: "#006b5f",
    dashed: false,
  },
];

export const nodeColor = (kind: NodeKind): string =>
  nodeKinds.find((item) => item.kind === kind)?.color ?? "#7a7a7a";

export const edgeStyle = (kind: EdgeKind) =>
  edgeKinds.find((item) => item.kind === kind);
