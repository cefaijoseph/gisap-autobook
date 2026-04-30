import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { BookingLog, ScheduleService } from '../schedule.service';

@Component({
  selector: 'app-booking-logs',
  standalone: true,
  imports: [
    CommonModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatCardModule,
  ],
  templateUrl: './booking-logs.component.html',
  styleUrl: './booking-logs.component.scss'
})
export class BookingLogsComponent implements OnInit {
  logs: BookingLog[] = [];
  loading = false;
  scheduleId = 0;
  displayedColumns = ['runAt', 'status', 'error', 'screenshot'];

  constructor(
    private svc: ScheduleService,
    private route: ActivatedRoute,
    public router: Router
  ) {}

  ngOnInit(): void {
    this.scheduleId = +(this.route.snapshot.paramMap.get('id') ?? '0');
    if (this.scheduleId) { this.load(); }
  }

  load(): void {
    this.loading = true;
    this.svc.getLogs(this.scheduleId).subscribe({
      next: logs => { this.logs = logs; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }
}

