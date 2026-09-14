import { createContext, useContext } from "react";
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import {
  Button,
  ButtonColor,
  ButtonSize,
  Link,
  Tag,
  Colors,
} from "@kentico/xperience-admin-components";

import { nodeColor, type NodeKind } from "../content-model-graph/model";
import type { ContentItemRelationshipItemDto } from "./model";

export type RelationshipExpandDirection = "incoming" | "outgoing";
export type RelationshipExpandStatus = "collapsed" | "expanded" | "loading";

export interface RelationshipNodeData extends Record<string, unknown> {
  readonly item: ContentItemRelationshipItemDto;
  readonly isRoot: boolean;
  readonly horizontal: boolean;
  readonly canExpand: boolean;
  readonly incomingStatus: RelationshipExpandStatus;
  readonly outgoingStatus: RelationshipExpandStatus;
  readonly onExpand: (direction: RelationshipExpandDirection) => void;
}

export type RelationshipNode = Node<RelationshipNodeData, "relationshipNode">;

export const RELATIONSHIP_NODE_WIDTH = 280;

const kindLabels: Readonly<Record<string, string>> = {
  website: "Page",
  reusable: "Reusable",
  email: "Email",
  headless: "Headless",
  objectType: "Object",
};

const formatKind = (kind: string) => kindLabels[kind] ?? kind;

const hasText = (value: string | null | undefined): value is string =>
  Boolean(value?.trim());

export const estimateRelationshipNodeHeight = (
  item: ContentItemRelationshipItemDto,
  canExpand: boolean,
  isRoot: boolean,
) => {
  let height = 64;

  if (isRoot) height += 24;
  if (item.isDefaultLanguageFallback) height += 28;
  if (hasText(item.contentTypeDisplayName)) height += 34;
  if (hasText(item.locationName)) height += 34;
  if (hasText(item.codeName)) height += 28;
  if (hasText(item.adminUrl) || hasText(item.liveUrl)) height += 40;
  if (canExpand) height += 40;

  return height;
};

export const RelationshipNodeSearchContext = createContext("");

const expandIcon = (status: RelationshipExpandStatus) =>
  status === "expanded" ? "xp-rotate-right" : "xp-plus-circle";

const expandTitle = (
  direction: RelationshipExpandDirection,
  status: RelationshipExpandStatus,
) => {
  const noun =
    direction === "incoming" ? "incoming references" : "outgoing references";

  return status === "expanded" ? `Refresh ${noun}` : `Load ${noun}`;
};

const expandLabel = (direction: RelationshipExpandDirection) =>
  direction === "incoming" ? "Incoming" : "Outgoing";

const ExpandButton = ({
  direction,
  status,
  onExpand,
}: {
  readonly direction: RelationshipExpandDirection;
  readonly status: RelationshipExpandStatus;
  readonly onExpand: (direction: RelationshipExpandDirection) => void;
}) => (
  <Button
    className="cmg-relationships-node__expand"
    label={expandLabel(direction)}
    icon={expandIcon(status)}
    title={expandTitle(direction, status)}
    size={ButtonSize.XS}
    color={ButtonColor.Quinary}
    inProgress={status === "loading"}
    disabled={status === "loading"}
    onClick={() => onExpand(direction)}
  />
);

export const RelationshipNodeComponent = ({
  data,
}: NodeProps<RelationshipNode>) => {
  const search = useContext(RelationshipNodeSearchContext);
  const {
    item,
    isRoot,
    horizontal,
    canExpand,
    incomingStatus,
    outgoingStatus,
    onExpand,
  } = data;
  const dimmed =
    !isRoot &&
    search.length > 0 &&
    !`${item.displayName} ${item.codeName}`.toLowerCase().includes(search);

  return (
    <div
      className={`cmg-relationships-node${
        isRoot ? " cmg-relationships-node--root" : ""
      }${dimmed ? " cmg-relationships-node--dimmed" : ""}`}
      style={{ borderLeftColor: nodeColor(item.kind as NodeKind) }}
    >
      <Handle
        type="target"
        position={horizontal ? Position.Left : Position.Top}
      />
      {isRoot && (
        <div className="cmg-relationships-node__current">Current item</div>
      )}
      <div className="cmg-relationships-node__header">
        <div className="cmg-relationships-node__name">{item.displayName}</div>
        {hasText(item.kind) && (
          <Tag
            label={formatKind(item.kind)}
            background={{ color: Colors.BackgroundTagWarmGrey }}
            readOnly
          />
        )}
      </div>
      {item.isDefaultLanguageFallback && (
        <span
          className="cmg-relationships-node__fallback"
          title="No variant exists for the current language, using default language fallback"
        >
          {hasText(item.fallbackLanguageCode)
            ? item.fallbackLanguageCode
            : "Default"}
        </span>
      )}
      {(hasText(item.contentTypeDisplayName) || hasText(item.locationName)) && (
        <dl className="cmg-relationships-node__facts">
          {hasText(item.contentTypeDisplayName) && (
            <div className="cmg-relationships-node__fact">
              <dt>Content type</dt>
              <dd>
                {hasText(item.contentTypeAdminUrl) ? (
                  <span className="nodrag nopan">
                    <Link
                      href={item.contentTypeAdminUrl}
                      text={item.contentTypeDisplayName}
                      target="_self"
                    />
                  </span>
                ) : (
                  item.contentTypeDisplayName
                )}
              </dd>
            </div>
          )}
          {hasText(item.locationName) && (
            <div className="cmg-relationships-node__fact">
              <dt>
                {item.locationKind === "Content hub" ? "Workspace" : "Channel"}
              </dt>
              <dd>
                {item.locationName}
                {hasText(item.locationKind) &&
                  item.locationKind !== "Content hub" && (
                    <span className="cmg-relationships-node__location-kind">
                      {item.locationKind}
                    </span>
                  )}
              </dd>
            </div>
          )}
        </dl>
      )}
      {hasText(item.codeName) && (
        <div className="cmg-relationships-node__code">{item.codeName}</div>
      )}
      {(hasText(item.adminUrl) || hasText(item.liveUrl)) && (
        <div className="cmg-relationships-node__actions nodrag nopan">
          {hasText(item.adminUrl) && (
            <Link href={item.adminUrl} text="Open in admin" target="_self" />
          )}
          {hasText(item.liveUrl) && (
            <Link href={item.liveUrl} text="View live" target="_blank" />
          )}
        </div>
      )}
      {canExpand && (
        <div className="cmg-relationships-node__expand-row nodrag nopan">
          <ExpandButton
            direction="incoming"
            status={incomingStatus}
            onExpand={onExpand}
          />
          <ExpandButton
            direction="outgoing"
            status={outgoingStatus}
            onExpand={onExpand}
          />
        </div>
      )}
      <Handle
        type="source"
        position={horizontal ? Position.Right : Position.Bottom}
      />
    </div>
  );
};
