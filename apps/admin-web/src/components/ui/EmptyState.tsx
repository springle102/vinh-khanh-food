export const EmptyState = ({
  title,
  description,
}: {
  title: string;
  description: string;
}) => (
  <div className="rounded-3xl border border-dashed border-sand-200 bg-sand-50/60 px-6 py-10 text-center">
    <h3 className="text-lg font-semibold text-ink-900">{title}</h3>
    <p className="mx-auto mt-2 max-w-xl text-sm leading-6 text-ink-500">{description}</p>
  </div>
);
