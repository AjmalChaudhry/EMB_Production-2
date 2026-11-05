import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { GenProductComponent } from './gen-product.component';
import { GenProductService } from '../services/gen-products.service';
import { ProductViewModel } from '../models/product.dto';

describe('GenProductComponent', () => {
  let component: GenProductComponent;
  let fixture: ComponentFixture<GenProductComponent>;

  // 🔹 Mock service
  const mockService = {
    listPaged: jasmine.createSpy('listPaged').and.returnValue(
      of({ items: [], total: 0 })
    ),
    add: jasmine.createSpy('add').and.returnValue(of({})),
    update: jasmine.createSpy('update').and.returnValue(of({})),
    delete: jasmine.createSpy('delete').and.returnValue(of({}))
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GenProductComponent],
      providers: [{ provide: GenProductService, useValue: mockService }]
    }).compileComponents();

    fixture = TestBed.createComponent(GenProductComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should start with empty product list', () => {
    expect(component.products).toEqual([]);
    expect(component.total).toBe(0);
  });

  it('should load products from service', () => {
    const mockProducts: ProductViewModel[] = [
      { Id: 1, Prod_Name: 'Test', Prod_Code: 'P001', Active: true } as any
    ];
    mockService.listPaged.and.returnValue(
      of({ items: mockProducts, total: 1 })
    );

    component.loadProducts();
    expect(component.products.length).toBe(1);
    expect(component.total).toBe(1);
    expect(mockService.listPaged).toHaveBeenCalled();
  });

  it('should handle loadProducts error', () => {
    mockService.listPaged.and.returnValue(throwError(() => new Error('API fail')));

    component.loadProducts();
    expect(component.loading).toBeFalse();
  });

  it('should reset page and search on clearSearch()', () => {
    component.page = 5;
    component.search = 'abc';
    component.clearSearch();
    expect(component.page).toBe(1);
    expect(component.search).toBe('');
  });

  it('should call deleteProduct()', () => {
    component.selectedProduct = { Id: 99 } as any;
    component.deleteProduct();
    expect(mockService.delete).toHaveBeenCalledWith(99);
  });
});
