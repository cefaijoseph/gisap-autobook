import { Routes } from '@angular/router';
import { ScheduleListComponent } from './schedule-list/schedule-list.component';
import { ScheduleFormComponent } from './schedule-form/schedule-form.component';
import { BookingLogsComponent } from './booking-logs/booking-logs.component';

export const routes: Routes = [
  { path: '', component: ScheduleListComponent },
  { path: 'schedules/new', component: ScheduleFormComponent },
  { path: 'schedules/:id/edit', component: ScheduleFormComponent },
  { path: 'schedules/:id/logs', component: BookingLogsComponent },
  { path: '**', redirectTo: '' },
];
