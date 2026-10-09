import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  viewChild,
  input,
  effect,
  OnDestroy,
  AfterViewInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ClusterNode } from '../../../core/models/cluster-node.model';

interface CanvasPartitionPoint {
  x: number;
  y: number;
  nodeId: string;
  clusterId: string;
  hash: number;
  angle: number;
  color: string;
}

@Component({
  selector: 'app-hash-ring-canvas',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="canvas-wrapper">
      <div class="ring-controls">
        <span class="control-label">Consistent Hash Ring Partitioning (360° Ring, Virtual Shards: 64/node)</span>
        <div class="legend">
          <span class="legend-item"><span class="legend-dot normal"></span> Standard Node</span>
          <span class="legend-item"><span class="legend-dot hotspot"></span> High Density Hotspot</span>
        </div>
      </div>
      <div class="canvas-container" #containerRef>
        <canvas #canvasRef (mousemove)="onCanvasMouseMove($event)" (mouseleave)="hoveredPoint = null"></canvas>
      </div>
      @if (hoveredPoint) {
        <div class="tooltip-card">
          <div class="tooltip-header">Partition Hash: <code>0x{{ hoveredPoint.hash.toString(16).toUpperCase() }}</code></div>
          <div class="tooltip-row">Node: <strong>{{ hoveredPoint.nodeId }}</strong></div>
          <div class="tooltip-row">Cluster: <strong>{{ hoveredPoint.clusterId }}</strong></div>
          <div class="tooltip-row">Ring Angle: <strong>{{ (hoveredPoint.angle * 180 / Math.PI).toFixed(1) }}°</strong></div>
        </div>
      }
    </div>
  `,
  styles: [`
    .canvas-wrapper {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px;
      display: flex;
      flex-direction: column;
      align-items: center;
      position: relative;
    }

    .ring-controls {
      width: 100%;
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
      flex-wrap: wrap;
      gap: 8px;
    }

    .control-label {
      font-size: 0.85rem;
      font-weight: 600;
      color: #94a3b8;
    }

    .legend {
      display: flex;
      gap: 16px;
      font-size: 0.75rem;
      color: #94a3b8;
    }

    .legend-item {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .legend-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .legend-dot.normal { background: #38bdf8; }
    .legend-dot.hotspot { background: #f87171; }

    .canvas-container {
      width: 100%;
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: 380px;
    }

    canvas {
      cursor: crosshair;
    }

    .tooltip-card {
      position: absolute;
      bottom: 20px;
      left: 20px;
      background: #0f172a;
      border: 1px solid #38bdf8;
      border-radius: 6px;
      padding: 10px 14px;
      font-size: 0.8rem;
      box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.5);
      pointer-events: none;
    }

    .tooltip-header {
      color: #38bdf8;
      margin-bottom: 4px;
    }

    .tooltip-row {
      color: #94a3b8;
      margin-top: 2px;
    }

    .tooltip-row strong {
      color: #f8fafc;
    }
  `]
})
export class HashRingCanvasComponent implements AfterViewInit, OnDestroy {
  readonly nodes = input<ClusterNode[]>([]);
  readonly canvasRef = viewChild<ElementRef<HTMLCanvasElement>>('canvasRef');
  readonly containerRef = viewChild<ElementRef<HTMLDivElement>>('containerRef');

  readonly Math = Math;
  hoveredPoint: CanvasPartitionPoint | null = null;

  private points: CanvasPartitionPoint[] = [];
  private animFrameId: number | null = null;
  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    effect(() => {
      // Re-calculate points and render when nodes input changes
      const currentNodes = this.nodes();
      this.recomputePartitions(currentNodes);
      this.scheduleRender();
    });
  }

  ngAfterViewInit(): void {
    const container = this.containerRef()?.nativeElement;
    if (container && typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => {
        this.scheduleRender();
      });
      this.resizeObserver.observe(container);
    }
    this.scheduleRender();
  }

  private recomputePartitions(nodeList: ClusterNode[]): void {
    this.points = [];
    if (!nodeList || nodeList.length === 0) return;

    // FNV-1a deterministic hash implementation for ultra-fast ring distribution
    const virtualShardsPerNode = 64;
    const colors = ['#38bdf8', '#34d399', '#a78bfa', '#f472b6', '#fbbf24', '#4ade80'];

    let colorIdx = 0;
    const nodeColorMap = new Map<string, string>();

    for (const node of nodeList) {
      if (!nodeColorMap.has(node.instanceId)) {
        nodeColorMap.set(node.instanceId, colors[colorIdx % colors.length]);
        colorIdx++;
      }
      const nodeColor = nodeColorMap.get(node.instanceId)!;

      for (let i = 0; i < virtualShardsPerNode; i++) {
        const key = `${node.clusterId || 'default'}:${node.appId}:${node.instanceId}:${i}`;
        // Fast FNV-1a 32-bit hash
        let hash = 0x811c9dc5;
        for (let j = 0; j < key.length; j++) {
          hash ^= key.charCodeAt(j);
          hash = Math.imul(hash, 0x01000193);
        }
        // Unsigned 32-bit
        const uHash = hash >>> 0;
        const angle = (uHash / 0xffffffff) * (2 * Math.PI);

        this.points.push({
          x: 0,
          y: 0,
          nodeId: node.instanceId,
          clusterId: node.clusterId || 'default',
          hash: uHash,
          angle,
          color: nodeColor
        });
      }
    }

    // Sort partitions by angle
    this.points.sort((a, b) => a.angle - b.angle);
  }

  scheduleRender(): void {
    if (this.animFrameId !== null) {
      cancelAnimationFrame(this.animFrameId);
    }
    this.animFrameId = requestAnimationFrame(() => {
      this.render();
      this.animFrameId = null;
    });
  }

  private render(): void {
    const canvas = this.canvasRef()?.nativeElement;
    const container = this.containerRef()?.nativeElement;
    if (!canvas || !container) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const dpr = typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1;
    const size = Math.min(container.clientWidth || 380, 500);

    canvas.width = size * dpr;
    canvas.height = size * dpr;
    canvas.style.width = `${size}px`;
    canvas.style.height = `${size}px`;

    ctx.save();
    ctx.scale(dpr, dpr);

    const centerX = size / 2;
    const centerY = size / 2;
    const radius = size * 0.38;

    // Clear
    ctx.clearRect(0, 0, size, size);

    // Draw main ring
    ctx.beginPath();
    ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
    ctx.strokeStyle = '#334155';
    ctx.lineWidth = 3;
    ctx.stroke();

    // Draw inner glow ring
    ctx.beginPath();
    ctx.arc(centerX, centerY, radius * 0.88, 0, 2 * Math.PI);
    ctx.strokeStyle = 'rgba(56, 189, 248, 0.08)';
    ctx.lineWidth = 1;
    ctx.stroke();

    // Center label
    ctx.fillStyle = '#94a3b8';
    ctx.font = '600 12px monospace';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(`${this.nodes().length} NODES`, centerX, centerY - 10);
    ctx.fillStyle = '#38bdf8';
    ctx.font = '700 11px monospace';
    ctx.fillText(`${this.points.length} SHARDS`, centerX, centerY + 10);

    // Draw partition tick marks
    for (let i = 0; i < this.points.length; i++) {
      const pt = this.points[i];
      const cos = Math.cos(pt.angle);
      const sin = Math.sin(pt.angle);

      // Check density around this angle to detect hotspots
      const nextPt = this.points[(i + 1) % this.points.length];
      let diff = nextPt.angle - pt.angle;
      if (diff < 0) diff += 2 * Math.PI;

      // Unusually close shards represent dense hotspot partition
      const isHotspot = diff < (2 * Math.PI / (this.points.length * 3));

      const innerR = radius - (isHotspot ? 10 : 6);
      const outerR = radius + (isHotspot ? 10 : 6);

      const x1 = centerX + cos * innerR;
      const y1 = centerY + sin * innerR;
      const x2 = centerX + cos * outerR;
      const y2 = centerY + sin * outerR;

      pt.x = centerX + cos * radius;
      pt.y = centerY + sin * radius;

      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.strokeStyle = isHotspot ? '#f87171' : pt.color;
      ctx.lineWidth = isHotspot ? 2 : 1.2;
      ctx.stroke();
    }

    ctx.restore();
  }

  onCanvasMouseMove(event: MouseEvent): void {
    const canvas = this.canvasRef()?.nativeElement;
    if (!canvas || this.points.length === 0) return;

    const rect = canvas.getBoundingClientRect();
    const mx = event.clientX - rect.left;
    const my = event.clientY - rect.top;

    let closest: CanvasPartitionPoint | null = null;
    let minDist = 16; // 16px hover radius

    for (const pt of this.points) {
      const dist = Math.hypot(pt.x - mx, pt.y - my);
      if (dist < minDist) {
        minDist = dist;
        closest = pt;
      }
    }

    this.hoveredPoint = closest;
  }

  ngOnDestroy(): void {
    if (this.animFrameId !== null) {
      cancelAnimationFrame(this.animFrameId);
    }
    if (this.resizeObserver) {
      this.resizeObserver.disconnect();
    }
  }
}
