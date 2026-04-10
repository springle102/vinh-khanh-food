import type { SelectHTMLAttributes } from "react";
import { cn } from "../../lib/utils";

export const Select = ({
  autoComplete,
  className,
  ...props
}: SelectHTMLAttributes<HTMLSelectElement>) => (
  <select
    {...props}
    autoComplete={autoComplete ?? "off"}
    className={cn("field-input appearance-none", className)}
  />
);
