import type { PropsWithChildren } from "react";
import { Button } from "./Button";

export const Modal = ({
  children,
  title,
  description,
  open,
  onClose,
  maxWidthClassName = "max-w-4xl",
}: PropsWithChildren<{
  title: string;
  description?: string;
  open: boolean;
  onClose: () => void;
  maxWidthClassName?: string;
}>) => {
  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-ink-900/50 p-4">
      <div className={`panel-surface mt-10 w-full ${maxWidthClassName} p-0`}>
        <div className="flex items-start justify-between border-b border-sand-100 px-6 py-5">
          <div>
            <h3 className="text-xl font-semibold text-ink-900">{title}</h3>
            {description ? <p className="mt-1 text-sm text-ink-500">{description}</p> : null}
          </div>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
        </div>
        <div className="px-6 py-5">{children}</div>
      </div>
    </div>
  );
};
