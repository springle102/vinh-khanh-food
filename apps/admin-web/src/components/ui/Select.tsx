import type { SelectHTMLAttributes } from "react";
import { cn } from "../../lib/utils";

export const Select = ({
  className,
  ...props
}: SelectHTMLAttributes<HTMLSelectElement>) => (
  <select className={cn("field-input appearance-none", className)} {...props} />
);
