import { Injectable } from '@angular/core';
import { ApiError, messageFromProblem } from './api-error';
import { ChatRequest, ChatStreamEvent } from './api.models';

/**
 * Client for POST /api/chat/stream. EventSource only supports GET, so the
 * Server-Sent Events are read straight from the fetch response body.
 */
@Injectable({ providedIn: 'root' })
export class ChatApi {
  async *stream(request: ChatRequest, signal: AbortSignal): AsyncGenerator<ChatStreamEvent> {
    let response: Response;
    try {
      response = await fetch('/api/chat/stream', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
        body: JSON.stringify(request),
        signal,
      });
    } catch (error) {
      if (signal.aborted) throw error;
      throw new ApiError("Can't reach the CareerCopilot API. Is it running?");
    }

    if (!response.ok || !response.body) {
      const body = await response.json().catch(() => undefined);
      throw new ApiError(messageFromProblem(body) ?? `Request failed (${response.status}).`, response.status);
    }

    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = '';

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;

      buffer += value.replace(/\r\n/g, '\n');
      let boundary: number;
      while ((boundary = buffer.indexOf('\n\n')) !== -1) {
        const event = parseEvent(buffer.slice(0, boundary));
        buffer = buffer.slice(boundary + 2);
        if (event) yield event;
      }
    }
  }
}

function parseEvent(block: string): ChatStreamEvent | null {
  const data = block
    .split('\n')
    .filter((line) => line.startsWith('data:'))
    .map((line) => line.slice(5).trimStart())
    .join('\n');

  return data ? (JSON.parse(data) as ChatStreamEvent) : null;
}
