import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Schedule {
  id: number;
  name: string;
  resourceId: string;
  isActive: boolean;
  triggerDayOfWeek: number;
  triggerTime: string;   // "HH:MM:SS" – .NET TimeSpan JSON
  daysInAdvance: number;
  startHour: number;
  endHour: number;
  numberOfPersons: number;
  retryCount: number;
  retryDelaySeconds: number;
  lastRunAt: string | null;
  nextRunAt: string | null;
  lastRunStatus: string | null;
}

export interface BookingLog {
  id: number;
  scheduleId: number;
  runAt: string;
  status: string;
  errorMessage: string | null;
  screenshotPath: string | null;
}

@Injectable({ providedIn: 'root' })
export class ScheduleService {
  private readonly base = '/api/schedules';
  private readonly logsBase = '/api/bookinglogs';

  constructor(private http: HttpClient) {}

  getAll(): Observable<Schedule[]> {
    return this.http.get<Schedule[]>(this.base);
  }

  getById(id: number): Observable<Schedule> {
    return this.http.get<Schedule>(`${this.base}/${id}`);
  }

  create(s: Partial<Schedule>): Observable<Schedule> {
    return this.http.post<Schedule>(this.base, s);
  }

  update(id: number, s: Partial<Schedule>): Observable<Schedule> {
    return this.http.put<Schedule>(`${this.base}/${id}`, s);
  }

  toggle(id: number): Observable<Schedule> {
    return this.http.patch<Schedule>(`${this.base}/${id}/toggle`, {});
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  getLogs(scheduleId: number): Observable<BookingLog[]> {
    return this.http.get<BookingLog[]>(`${this.logsBase}/schedule/${scheduleId}`);
  }
}
