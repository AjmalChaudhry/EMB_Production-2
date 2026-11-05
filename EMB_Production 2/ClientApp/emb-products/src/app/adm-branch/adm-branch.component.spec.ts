import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { AdmBranchComponent } from './adm-branch.component';

describe('AdmBranchComponent', () => {
  let component: AdmBranchComponent;
  let fixture: ComponentFixture<AdmBranchComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FormsModule, ReactiveFormsModule, AdmBranchComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(AdmBranchComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the AdmBranchComponent', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize default values correctly', () => {
    expect(component.page).toBe(1);
    expect(component.branches).toEqual([]);
  });

  it('should have form defined', () => {
    expect(component.form).toBeDefined();
  });
});
