import { useMemo, useState, type ReactNode } from "react";
import { cn } from "../../lib/utils";
import { Button } from "./Button";

export interface DataColumn<T> {
  key: string;
  header: string;
  widthClassName?: string;
  cellClassName?: string;
  render: (row: T) => ReactNode;
}

export const DataTable = <T,>({
  data,
  columns,
  rowKey,
  pageSize = 6,
  tableClassName,
}: {
  data: T[];
  columns: DataColumn<T>[];
  rowKey: (row: T) => string;
  pageSize?: number;
  tableClassName?: string;
}) => {
  const [page, setPage] = useState(1);
  const pageCount = Math.max(1, Math.ceil(data.length / pageSize));

  const pagedData = useMemo(() => {
    const start = (page - 1) * pageSize;
    return data.slice(start, start + pageSize);
  }, [data, page, pageSize]);

  return (
    <div className="overflow-hidden rounded-3xl border border-sand-100">
      <div className="overflow-x-auto">
        <table className={cn("min-w-full divide-y divide-sand-100", tableClassName)}>
          <thead className="bg-sand-50/80">
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  className={cn(
                    "px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-ink-500",
                    column.widthClassName,
                  )}
                >
                  {column.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-sand-100 bg-white">
            {pagedData.map((row) => (
              <tr key={rowKey(row)} className="align-top">
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={cn(
                      "px-4 py-4 text-sm text-ink-700",
                      column.widthClassName,
                      column.cellClassName,
                    )}
                  >
                    {column.render(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="flex items-center justify-between bg-sand-50/70 px-4 py-3 text-sm text-ink-500">
        <span>
          Hiển thị {pagedData.length} / {data.length} bản ghi
        </span>
        <div className="flex items-center gap-2">
          <Button variant="ghost" disabled={page === 1} onClick={() => setPage((value) => value - 1)}>
            Trước
          </Button>
          <span>
            Trang {page} / {pageCount}
          </span>
          <Button
            variant="ghost"
            disabled={page >= pageCount}
            onClick={() => setPage((value) => value + 1)}
          >
            Sau
          </Button>
        </div>
      </div>
    </div>
  );
};
