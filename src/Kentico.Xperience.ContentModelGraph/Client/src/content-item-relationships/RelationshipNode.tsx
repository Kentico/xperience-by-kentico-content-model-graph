import { createContext, useContext } from "react";
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import {
  Button,
  ButtonColor,
  ButtonSize,
  Icon,
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
  taxonomy: "Tag",
  forms: "Form",
};

const formatKind = (kind: string) => kindLabels[kind] ?? kind;

const hasText = (value: string | null | undefined): value is string =>
  Boolean(value?.trim());

// The platform's own wording on the Content hub "Used in" tab, where a row for an item the user cannot
// read is shown in full but with its link disabled. Shared by the disabled link's tooltip and the icon
// beside it so the two cannot drift apart.
export const RESTRICTED_TOOLTIP =
  "You don't have permission to read this item";

const hasLinks = (item: ContentItemRelationshipItemDto) =>
  hasText(item.adminUrl) || hasText(item.liveUrl);

// A restricted item never carries links, so it gets a disabled stand-in row in their place: "no link"
// and "you may not open this" must not look the same.
const hasActionRow = (item: ContentItemRelationshipItemDto) =>
  hasLinks(item) || Boolean(item.isRestricted);

// The "Used in" tab lists every location under one Channel column. Only a content hub item is labelled by
// its workspace here, and only when the user may read it: for a restricted one the server sends the kind
// ("Content hub") as the name, as that tab does, so the label follows it. Channel names are not withheld.
const locationLabel = (item: ContentItemRelationshipItemDto) =>
  !item.isRestricted && item.locationKind === "Content hub"
    ? "Workspace"
    : "Channel";

export const estimateRelationshipNodeHeight = (
  item: ContentItemRelationshipItemDto,
  canExpand: boolean,
  isRoot: boolean,
) => {
  let height = 64;

  if (isRoot) height += 24;
  if (item.isMissing) height += 28;
  if (item.isDefaultLanguageFallback) height += 28;
  if (hasText(item.contentTypeDisplayName)) height += 34;
  if (hasText(item.locationName)) height += 34;
  if (hasText(item.codeName)) height += 28;
  if (hasActionRow(item)) height += 40;
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
      }${item.isMissing ? " cmg-relationships-node--missing" : ""}${
        dimmed ? " cmg-relationships-node--dimmed" : ""
      }`}
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
      {item.isMissing && (
        <div className="cmg-relationships-node__missing">
          <Tag
            label="Missing"
            tooltipText="This item no longer exists. The details shown were loaded before it was deleted."
            background={{ color: Colors.BackgroundTagRose }}
            readOnly
          />
        </div>
      )}
      {item.isDefaultLanguageFallback && (
        <span
          className="cmg-relationships-node__fallback"
          title="No variant exists for the current language, using default language fallback"
        >
          {hasText(item.languageCode) ? item.languageCode : "Default"}
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
              <dt>{locationLabel(item)}</dt>
              <dd>
                {item.locationName}
                {hasText(item.locationKind) &&
                  item.locationKind !== "Content hub" &&
                  item.locationKind !== item.locationName && (
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
      {hasLinks(item) && (
        <div className="cmg-relationships-node__actions nodrag nopan">
          {hasText(item.adminUrl) && (
            <Link href={item.adminUrl} text="Open in admin" target="_self" />
          )}
          {hasText(item.liveUrl) && (
            <Link href={item.liveUrl} text="View live" target="_blank" />
          )}
        </div>
      )}
      {!hasLinks(item) && item.isRestricted && (
        <div className="cmg-relationships-node__actions nodrag nopan">
          <span
            className="cmg-relationships-node__action--disabled"
            title={RESTRICTED_TOOLTIP}
            aria-disabled="true"
          >
            {/*
             * Decorative: the span above already carries the name ("Open in admin") and the reason (the
             * title), so labelling the icon as well would read the state out twice.
             */}
            <span
              className="cmg-relationships-node__action-icon"
              aria-hidden="true"
            >
              <Icon name="xp-ban-sign" />
            </span>
            <span className="cmg-relationships-node__action-text">
              Open in admin
            </span>
          </span>
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
