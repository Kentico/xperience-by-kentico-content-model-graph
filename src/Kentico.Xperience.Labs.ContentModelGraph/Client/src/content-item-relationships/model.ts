export interface ContentItemRelationshipItemDto {
  readonly itemId?: number | null;
  readonly identifier?: string | null;
  readonly displayName: string;
  readonly codeName: string;
  readonly contentTypeDisplayName: string;
  readonly contentTypeCodeName: string;
  readonly contentTypeAdminUrl?: string | null;
  readonly kind: string;
  readonly locationName?: string | null;
  readonly locationKind?: string | null;
  readonly adminUrl?: string | null;
  readonly liveUrl?: string | null;
  readonly isDefaultLanguageFallback?: boolean;
  // The content language the item's metadata was actually read in. Set for every content item, whether or
  // not it fell back, so an export records what each node resolved to. Null for nodes with no language of
  // their own (a form) and for items with no language metadata in the fallback chain.
  readonly languageCode?: string | null;
  // Set when the item no longer exists. The node keeps the metadata already loaded for it so the
  // broken reference stays diagnosable, but it is terminal - it cannot be expanded any further.
  readonly isMissing?: boolean;
  // Set when the current user is not allowed to view the item. The node stays in the graph so the
  // relationship remains visible - the platform's own "Used in" tab lists such an item too - but the
  // server withholds what the user may not see: no links, no codeName, and a locationName that carries
  // only the kind of place ("Content hub", "Website") rather than the workspace or channel. It cannot
  // be expanded.
  readonly isRestricted?: boolean;
}

export interface ContentItemRelationshipDto {
  readonly id: string;
  readonly relatedItem: ContentItemRelationshipItemDto;
  readonly fieldLabel: string;
  readonly fieldCodeName: string;
  // Set for references stored in the Page Builder configuration: the editable area, section, widget,
  // personalization variant and property the reference was found at, one line per occurrence.
  readonly fieldPath?: string | null;
  readonly direction: string;
}

// Reports that one direction of the graph shows only part of what exists. Counts are of related items,
// not of edges: one item can be reached by several edges, and the server caps per item.
export interface ContentItemRelationshipTruncationDto {
  readonly direction: string;
  readonly shownItemCount: number;
  readonly totalItemCount: number;
}

export interface ContentItemRelationshipGraphDto {
  readonly rootItem: ContentItemRelationshipItemDto;
  readonly incoming: readonly ContentItemRelationshipDto[];
  readonly outgoing: readonly ContentItemRelationshipDto[];
  // The language the graph was requested in - what the viewer asked for, which the per-item codes can
  // differ from through fallback.
  readonly languageCode?: string | null;
  readonly truncations?: readonly ContentItemRelationshipTruncationDto[] | null;
}
