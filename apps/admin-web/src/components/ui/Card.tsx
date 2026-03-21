import type { HTMLAttributes, PropsWithChildren } from "react";
import { cn } from "../../lib/utils";

export const Card = ({
  children,
  className,
  ...props
}: PropsWithChildren<HTMLAttributes<HTMLDivElement>>) => (
  <div className={cn("panel-surface p-5", className)} {...props}>
    {children}
  </div>
);
