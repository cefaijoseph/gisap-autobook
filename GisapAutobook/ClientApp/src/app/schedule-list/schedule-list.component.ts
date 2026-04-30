import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Schedule, ScheduleService } from '../schedule.service';

const DAY_NAMES = ['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];

@Component({
  selector: 'app-schedule-list',
  standalone: true,
  imports: [
    CommonModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatTooltipModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './schedule-list.component.html',
  styleUrl: './schedule-list.component.scss'
})
export class ScheduleListComponent implements OnInit {
  schedules: Schedule[] = [];
  displayedColumns = ['name','resource','trigger','nextRun','lastStatus','active','actions'];
  loading = false;

  constructor(
    private svc: ScheduleService,
    private router: Router,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.svc.getAll().subscribe({
      next: s => { this.schedules = s; this.loading = false; },
      error: () => { this.loading = false; this.snack.open('Failed to load schedules', 'Close', { duration: 3000 }); }
    });
  }

  dayName(d: number): string { return DAY_NAMES[d] ?? '?'; }

  formatTime(t: string): string {
    if (!t) return '';
    const parts = t.split(':');
    return `${parts[0]}:${parts[1]}`;
  }

  toggle(s: Schedule): void {
    this.svc.toggle(s.id).subscribe({
      next: updated => {
        const idx = this.schedules.findIndex(x => x.id === s.id);
        if (idx >= 0) this.schedules[idx] = updated;
        this.schedules = [...this.schedules];
      },
      error: () => this.snack.open('Toggle failed', 'Close', { duration: 3000 })
    });
  }

  edit(id: number): void { this.router.navigate(['/schedules', id, 'edit']); }

  viewLogs(id: number): void { this.router.navigate(['/schedules', id, 'logs']); }

  delete(s: Schedule): void {
    if (!confirm(`Delete schedule "${s.name}"?`)) return;
    this.svc.delete(s.id).subscribe({
      next: () => { this.schedules = this.schedules.filter(x => x.id !== s.id); },
      error: () => this.snack.open('Delete failed', 'Close', { duration: 3000 })
    });
  }

  create(): void { this.router.navigate(['/schedules/new']); }
}

