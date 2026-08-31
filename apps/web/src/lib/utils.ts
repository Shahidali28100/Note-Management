import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Standard shadcn/ui class-merging helper — used by every generated component. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
