import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { Schedule, ScheduleService } from '../schedule.service';

@Component({
  selector: 'app-schedule-form',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatButtonModule,
    MatSnackBarModule,
    MatCardModule,
    MatIconModule,
  ],
  templateUrl: './schedule-form.component.html',
  styleUrl: './schedule-form.component.scss'
})
export class ScheduleFormComponent implements OnInit {
  isNew = true;
  saving = false;

  model: Partial<Schedule> = {
    name: '',
    resourceId: '241563',
    isActive: true,
    triggerDayOfWeek: 1,
    triggerTime: '07:00:00',
    daysInAdvance: 7,
    startHour: 8,
    endHour: 10,
    numberOfPersons: 4,
    retryCount: 2,
    retryDelaySeconds: 30,
  };

  days = [
    { value: 0, label: 'Sunday' },
    { value: 1, label: 'Monday' },
    { value: 2, label: 'Tuesday' },
    { value: 3, label: 'Wednesday' },
    { value: 4, label: 'Thursday' },
    { value: 5, label: 'Friday' },
    { value: 6, label: 'Saturday' },
  ];

  hours = Array.from({ length: 16 }, (_, i) => ({ value: i + 6, label: `${i + 6}:00` }));

  persons = [1, 2, 3, 4];

  constructor(
    private svc: ScheduleService,
    private route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isNew = false;
      this.svc.getById(+id).subscribe({
        next: s => {
          this.model = { ...s };
          // Normalize TimeSpan "HH:MM:SS" to input-friendly "HH:MM:SS"
        },
        error: () => this.router.navigate(['/'])
      });
    }
  }

  save(): void {
    this.saving = true;
    const op = this.isNew
      ? this.svc.create(this.model)
      : this.svc.update(this.model.id!, this.model);

    op.subscribe({
      next: () => {
        this.snack.open('Schedule saved', 'Close', { duration: 2000 });
        this.router.navigate(['/']);
      },
      error: () => {
        this.saving = false;
        this.snack.open('Save failed', 'Close', { duration: 3000 });
      }
    });
  }

  cancel(): void { this.router.navigate(['/']); }
}

