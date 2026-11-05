import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { FinishedStockService } from '../services/finished-stock.service';
import { FinishedStockRowDto } from '../shared/models/finished-stock.dto';

@Component({
  standalone: true,
  selector: 'app-finished-stock',
  templateUrl: './finished-stock.component.html',
  styleUrls: ['./finished-stock.component.css'],
  imports: [
    CommonModule,
    FormsModule
  ]
})
export class FinishedStockComponent implements OnInit {

  rows: FinishedStockRowDto[] = [];
  loading = false;

  constructor(private service: FinishedStockService) { }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;

    this.service.getAll().subscribe({
      next: res => {
        if (res?.success) {
          const data: any = res.data;
          this.rows = Array.isArray(data)
            ? data
            : data?.items || data?.Items || [];
        } else {
          this.rows = [];
        }
        this.loading = false;
      },
      error: err => {
        console.error(err);
        this.rows = [];
        this.loading = false;
      }
    });
  }

  reserve(row: FinishedStockRowDto): void { }
  release(row: FinishedStockRowDto): void { }
  adjust(row: FinishedStockRowDto): void { }
}
