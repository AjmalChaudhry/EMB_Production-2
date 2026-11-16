// src/app/shared/services/api-data.service.ts
import { Injectable } from '@angular/core';
import * as XLSX from 'xlsx';
import { saveAs } from 'file-saver';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

export interface ExportColumn {
  header: string;      // Column header in Excel/PDF/Print
  field: string;       // Field name from your data objects
}

@Injectable({
  providedIn: 'root'
})
export class ApiDataService {

  constructor() { }

  private escapeHtml(value: any): string {
    if (value === null || value === undefined) {
      return '';
    }
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  exportToExcel(data: any[], fileName: string, columns: ExportColumn[]): void {
    if (!data?.length) {
      alert('No data to export.');
      return;
    }

    const sheetData = data.map(item => {
      const row: any = {};
      columns.forEach(col => row[col.header] = item[col.field] ?? '');
      return row;
    });

    const ws = XLSX.utils.json_to_sheet(sheetData);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Sheet1');
    const wbout = XLSX.write(wb, { bookType: 'xlsx', type: 'array' });
    saveAs(new Blob([wbout], { type: 'application/octet-stream' }), `${fileName}_${new Date().toISOString().slice(0, 10)}.xlsx`);
  }

  exportToPDF(data: any[], fileName: string, columns: ExportColumn[]): void {
    if (!data?.length) {
      alert('No data to export.');
      return;
    }

    const doc = new jsPDF();
    doc.setFontSize(14);
    doc.text(fileName, 14, 15);

    const tableData = data.map(item => columns.map(col => item[col.field] ?? ''));

    autoTable(doc, {
      startY: 20,
      head: [columns.map(c => c.header)],
      body: tableData,
      styles: { fontSize: 8 },
      headStyles: { fillColor: [52, 58, 64] }
    });

    doc.save(`${fileName}.pdf`);
  }

  printTable(data: any[], columns: ExportColumn[], title: string = 'Table'): void {
    if (!data?.length) {
      alert('No data to print.');
      return;
    }

    const safeTitle = this.escapeHtml(title);

    const tableHtml = `
      <table border="1" style="width:100%; border-collapse: collapse;">
        <thead>
          <tr style="background-color:#343a40; color:white;">
            ${columns.map(c => `<th>${this.escapeHtml(c.header)}</th>`).join('')}
          </tr>
        </thead>
        <tbody>
          ${data.map(item => `
            <tr>
              ${columns.map(c => `<td>${this.escapeHtml(item[c.field] ?? '')}</td>`).join('')}
            </tr>
          `).join('')}
        </tbody>
      </table>
    `;

    const w = window.open('', '', 'width=1000,height=600');
    if (!w) return;

    w.document.write('<html><head><title>' + safeTitle + '</title>');
    w.document.write('<style>table {width:100%; border-collapse: collapse;} th, td {border:1px solid #ddd; padding:6px; text-align:left;} th {background-color:#343a40; color:white;}</style>');
    w.document.write('</head><body>');
    w.document.write('<h4>' + safeTitle + '</h4>');
    w.document.write(tableHtml);
    w.document.write('</body></html>');
    w.document.close();
    w.print();
  }
}
