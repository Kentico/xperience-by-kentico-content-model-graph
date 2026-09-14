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
  readonly fallbackLanguageCode?: string | null;
}

export interface ContentItemRelationshipDto {
  readonly id: string;
  readonly relatedItem: ContentItemRelationshipItemDto;
  readonly fieldLabel: string;
  readonly fieldCodeName: string;
  readonly direction: string;
}

export interface ContentItemRelationshipGraphDto {
  readonly rootItem: ContentItemRelationshipItemDto;
  readonly incoming: readonly ContentItemRelationshipDto[];
  readonly outgoing: readonly ContentItemRelationshipDto[];
}
