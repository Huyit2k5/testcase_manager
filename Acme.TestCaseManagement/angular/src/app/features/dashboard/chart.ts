import { BurnDownPoint, VelocityPoint } from '../../proxy/dtos';

/** The drawing area of a chart, in SVG units. */
export const CHART = { width: 640, height: 220, left: 36, right: 12, top: 12, bottom: 28 } as const;

const plotWidth = CHART.width - CHART.left - CHART.right;
const plotHeight = CHART.height - CHART.top - CHART.bottom;

export interface Tick { position: number; label: string }

/** A "nice" top for an axis: 1, 2, 5 times a power of ten that is not below the value (and at least 1). */
export function niceMax(value: number): number {
  if (value <= 1) { return 1; }
  const power = Math.pow(10, Math.floor(Math.log10(value)));
  const fraction = value / power;
  const step = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
  return step * power;
}

const xAt = (index: number, count: number) => CHART.left + (count <= 1 ? plotWidth / 2 : (index * plotWidth) / (count - 1));
const yAt = (value: number, max: number) => CHART.top + plotHeight - (Math.min(value, max) / max) * plotHeight;
const round = (n: number) => Math.round(n * 10) / 10;

/** An SVG path through the values; a missing value (null) lifts the pen, so a line stops where the data does. */
export function linePath(values: (number | null)[], max: number): string {
  let path = '';
  let pen = false;
  values.forEach((value, index) => {
    if (value === null) { pen = false; return; }
    path += `${pen ? 'L' : 'M'}${round(xAt(index, values.length))},${round(yAt(value, max))} `;
    pen = true;
  });
  return path.trim();
}

export interface BurnDownChart {
  max: number;
  ideal: string;
  actual: string;
  yTicks: Tick[];
  xTicks: Tick[];
  /** The x of the last day with data, or null when there is none. */
  todayX: number | null;
}

/** Labels roughly every `every` points, always including the first and the last. */
function xTicks(count: number, label: (index: number) => string, every: number): Tick[] {
  const ticks: Tick[] = [];
  for (let i = 0; i < count; i++) {
    if (i % every === 0 || i === count - 1) {
      ticks.push({ position: round(xAt(i, count)), label: label(i) });
    }
  }
  // The last label would sit on top of the one before it.
  if (ticks.length > 1 && ticks[ticks.length - 1].position - ticks[ticks.length - 2].position < 36) {
    ticks.splice(ticks.length - 2, 1);
  }
  return ticks;
}

function yTicks(max: number): Tick[] {
  const steps = max >= 4 ? 4 : max;
  return Array.from({ length: steps + 1 }, (_, i) => {
    const value = (max * i) / steps;
    return { position: round(yAt(value, max)), label: String(Math.round(value * 10) / 10) };
  });
}

/** `shortDate` turns a date of a point into its axis label. */
export function burnDownChart(points: BurnDownPoint[], shortDate: (iso: string) => string): BurnDownChart {
  const highest = Math.max(0, ...points.map(p => Math.max(p.remaining ?? 0, p.ideal)));
  const max = niceMax(highest);
  const lastWithData = points.reduce((found, p, i) => (p.remaining !== null ? i : found), -1);

  return {
    max,
    ideal: linePath(points.map(p => p.ideal), max),
    actual: linePath(points.map(p => p.remaining), max),
    yTicks: yTicks(max),
    xTicks: xTicks(points.length, i => shortDate(points[i].date), Math.max(1, Math.ceil(points.length / 8))),
    todayX: lastWithData < 0 ? null : round(xAt(lastWithData, points.length)),
  };
}

export interface Bar { x: number; width: number; passed: { y: number; height: number }; failed: { y: number; height: number }; other: { y: number; height: number }; attempts: number; date: string }

export interface VelocityChart { max: number; bars: Bar[]; yTicks: Tick[]; xTicks: Tick[] }

/** Stacked bars of the attempts of each day: failed at the bottom, then other results, then passed. */
export function velocityChart(points: VelocityPoint[], shortDate: (iso: string) => string): VelocityChart {
  const max = niceMax(Math.max(0, ...points.map(p => p.attempts)));
  const slot = plotWidth / Math.max(1, points.length);
  const width = Math.max(2, slot * 0.7);
  const base = CHART.top + plotHeight;
  const height = (value: number) => (value / max) * plotHeight;

  const bars = points.map((p, i): Bar => {
    const other = Math.max(0, p.attempts - p.passed - p.failed);
    const failedH = height(p.failed);
    const otherH = height(other);
    const passedH = height(p.passed);
    return {
      x: round(CHART.left + i * slot + (slot - width) / 2),
      width: round(width),
      failed: { y: round(base - failedH), height: round(failedH) },
      other: { y: round(base - failedH - otherH), height: round(otherH) },
      passed: { y: round(base - failedH - otherH - passedH), height: round(passedH) },
      attempts: p.attempts,
      date: p.date,
    };
  });

  const labelEvery = Math.max(1, Math.ceil(points.length / 8));
  const ticks: Tick[] = [];
  points.forEach((p, i) => {
    if (i % labelEvery === 0 || i === points.length - 1) {
      ticks.push({ position: round(CHART.left + i * slot + slot / 2), label: shortDate(p.date) });
    }
  });
  if (ticks.length > 1 && ticks[ticks.length - 1].position - ticks[ticks.length - 2].position < 36) {
    ticks.splice(ticks.length - 2, 1);
  }

  return { max, bars, yTicks: yTicks(max), xTicks: ticks };
}
