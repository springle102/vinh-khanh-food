import { cn } from "../../lib/utils";

const toneMap: Record<string, string> = {
  published: "bg-emerald-50 text-emerald-700 ring-emerald-100",
  active: "bg-emerald-50 text-emerald-700 ring-emerald-100",
  inactive: "bg-slate-100 text-slate-700 ring-slate-200",
  banned: "bg-rose-50 text-rose-700 ring-rose-100",
  ready: "bg-emerald-50 text-emerald-700 ring-emerald-100",
  approved: "bg-emerald-50 text-emerald-700 ring-emerald-100",
  rejected: "bg-rose-50 text-rose-700 ring-rose-100",
  draft: "bg-amber-50 text-amber-700 ring-amber-100",
  upcoming: "bg-amber-50 text-amber-700 ring-amber-100",
  pending: "bg-amber-50 text-amber-700 ring-amber-100",
  invited: "bg-amber-50 text-amber-700 ring-amber-100",
  processing: "bg-sky-50 text-sky-700 ring-sky-100",
  premium: "bg-amber-100 text-amber-900 ring-amber-200",
  basic: "bg-sky-50 text-sky-700 ring-sky-100",
  archived: "bg-slate-100 text-slate-700 ring-slate-200",
  suspended: "bg-rose-50 text-rose-700 ring-rose-100",
  hidden: "bg-rose-50 text-rose-700 ring-rose-100",
  expired: "bg-slate-100 text-slate-700 ring-slate-200",
  missing: "bg-rose-50 text-rose-700 ring-rose-100",
};

export const StatusBadge = ({
  status,
  label,
}: {
  status: string;
  label?: string;
}) => (
  <span
    className={cn(
      "inline-flex whitespace-nowrap rounded-full px-3 py-1 text-xs font-semibold ring-1 tracking-normal",
      toneMap[status] ?? "bg-sand-100 text-ink-700 ring-sand-200",
    )}
  >
    {label ?? status}
  </span>
);
