import { Component, input } from '@angular/core';

@Component({
  selector: 'ca-table',
  template: `
    <div class="table-scroll" tabindex="0">
      <table>
        <thead><tr>@for (cell of headers(); track $index) { <th scope="col">{{ cell }}</th> }</tr></thead>
        <tbody><ng-content /></tbody>
      </table>
    </div>
  `,
})
export class DataTable { readonly headers = input.required<string[]>(); }
