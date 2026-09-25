import { Pipe, PipeTransform } from '@angular/core';
import { marked } from 'marked';

marked.use({ gfm: true, breaks: true });

/**
 * Renders the assistant's Markdown. The output is bound through [innerHTML],
 * so Angular's sanitizer still strips anything unsafe.
 */
@Pipe({ name: 'markdown' })
export class MarkdownPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return value ? (marked.parse(value, { async: false }) as string) : '';
  }
}

@Pipe({ name: 'fileSize' })
export class FileSizePipe implements PipeTransform {
  transform(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}

const relativeFormat = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['day', 86_400_000],
  ['hour', 3_600_000],
  ['minute', 60_000],
];

@Pipe({ name: 'timeAgo' })
export class TimeAgoPipe implements PipeTransform {
  transform(value: string | number | null | undefined): string {
    if (value == null) return '';
    const elapsed = new Date(value).getTime() - Date.now();
    if (Math.abs(elapsed) > 7 * 86_400_000) return new Date(value).toLocaleDateString();
    for (const [unit, ms] of UNITS) {
      if (Math.abs(elapsed) >= ms) return relativeFormat.format(Math.round(elapsed / ms), unit);
    }
    return 'just now';
  }
}
