import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AppComponent } from './app.component';
import { ControlPlaneApiService } from './core/services/control-plane-api.service';

describe('AppComponent', () => {
  const mockApiService = {
    getHealth: () => of({
      role: 'Active',
      isLeader: true,
      leaseTtlSeconds: 4.0,
      status: 'Healthy'
    }),
    getTopology: () => of([])
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: ControlPlaneApiService, useValue: mockApiService }
      ]
    }).compileComponents();
  });

  it('should create the app shell', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render brand logo title', async () => {
    const fixture = TestBed.createComponent(AppComponent);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.brand-title')?.textContent).toContain('CENTRA');
  });

  it('should provide navigation links', async () => {
    const fixture = TestBed.createComponent(AppComponent);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const links = compiled.querySelectorAll('.nav-item');
    expect(links.length).toBeGreaterThanOrEqual(7);
  });
});
