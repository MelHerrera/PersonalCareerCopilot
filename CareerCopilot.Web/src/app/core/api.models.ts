// Mirrors the contracts in CareerCopilot.API/Contracts.

export type DocumentKind = 'resume' | 'jobPosting' | 'other';

export interface CareerDocument {
  name: string;
  kind: DocumentKind;
  /** 'text' for pasted text or a page imported from a link. */
  format: 'pdf' | 'text';
  sizeBytes: number;
  uploadedAt: string | null;
  chunkCount: number;
  isIndexed: boolean;
  /** The link a document was imported from, if any. */
  sourceUrl?: string;
}

export interface ChatTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  question: string;
  history?: ChatTurn[];
}

export interface ChatSource {
  fileName: string;
  kind: DocumentKind;
  excerpt: string;
  score: number;
}

export type ChatStreamEvent =
  | { type: 'sources'; sources: ChatSource[] }
  | { type: 'delta'; text: string }
  | { type: 'error'; text: string }
  | { type: 'done' };

export interface HealthStatus {
  status: string;
  azureOpenAIConfigured: boolean;
  blobStorageConfigured: boolean;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export const KIND_LABELS: Record<DocumentKind, string> = {
  resume: 'Résumé',
  jobPosting: 'Job posting',
  other: 'Other',
};
