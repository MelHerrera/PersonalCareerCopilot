import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { describeError } from './api-error';
import { ChatSource, ChatTurn } from './api.models';
import { ChatApi } from './chat.api';
import { readStorage, writeStorage } from './storage';

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  status: 'streaming' | 'done' | 'stopped' | 'error';
  sources?: ChatSource[];
  error?: string;
  createdAt: number;
}

const STORAGE_KEY = 'cc.conversation';
const MAX_HISTORY_TURNS = 10;

@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly api = inject(ChatApi);
  private abort?: AbortController;

  readonly messages = signal<ChatMessage[]>(this.restore());
  readonly isStreaming = computed(() => this.messages().some((m) => m.status === 'streaming'));
  readonly isEmpty = computed(() => this.messages().length === 0);

  constructor() {
    // Persist the conversation once each answer settles (not on every streamed token).
    effect(() => {
      if (!this.isStreaming()) writeStorage(STORAGE_KEY, this.messages());
    });
  }

  async send(question: string) {
    question = question.trim();
    if (!question || this.isStreaming()) return;

    const history = this.historyFrom(this.messages());
    this.messages.update((list) => [...list, this.create('user', question, 'done')]);
    await this.answer(question, history);
  }

  /** Re-asks the question that produced a failed or stopped answer. */
  async retry(assistantId: string) {
    const list = this.messages();
    const index = list.findIndex((m) => m.id === assistantId);
    const question = list[index - 1];
    if (index < 1 || question?.role !== 'user' || this.isStreaming()) return;

    this.messages.set(list.slice(0, index));
    await this.answer(question.content, this.historyFrom(list.slice(0, index - 1)));
  }

  stop() {
    this.abort?.abort();
  }

  clear() {
    this.stop();
    this.messages.set([]);
  }

  private async answer(question: string, history: ChatTurn[]) {
    const reply = this.create('assistant', '', 'streaming');
    this.messages.update((list) => [...list, reply]);

    this.abort = new AbortController();
    try {
      for await (const event of this.api.stream({ question, history }, this.abort.signal)) {
        switch (event.type) {
          case 'sources':
            this.patch(reply.id, () => ({ sources: event.sources }));
            break;
          case 'delta':
            this.patch(reply.id, (m) => ({ content: m.content + event.text }));
            break;
          case 'error':
            this.patch(reply.id, () => ({ status: 'error', error: event.text }));
            return;
        }
      }
      this.patch(reply.id, () => ({ status: 'done' }));
    } catch (error) {
      if (this.abort.signal.aborted) {
        this.patch(reply.id, () => ({ status: 'stopped' }));
      } else {
        this.patch(reply.id, () => ({ status: 'error', error: describeError(error) }));
      }
    }
  }

  private historyFrom(messages: ChatMessage[]): ChatTurn[] {
    return messages
      .filter((m) => m.status === 'done' && m.content)
      .slice(-MAX_HISTORY_TURNS)
      .map(({ role, content }) => ({ role, content }));
  }

  private patch(id: string, change: (message: ChatMessage) => Partial<ChatMessage>) {
    this.messages.update((list) => list.map((m) => (m.id === id ? { ...m, ...change(m) } : m)));
  }

  private create(role: ChatMessage['role'], content: string, status: ChatMessage['status']): ChatMessage {
    return { id: crypto.randomUUID(), role, content, status, createdAt: Date.now() };
  }

  private restore(): ChatMessage[] {
    const stored = readStorage<ChatMessage[]>(STORAGE_KEY);
    if (!Array.isArray(stored)) return [];
    // An answer that was mid-stream when the page closed can't be resumed.
    return stored.map((m) => (m.status === 'streaming' ? { ...m, status: 'stopped' } : m));
  }
}
