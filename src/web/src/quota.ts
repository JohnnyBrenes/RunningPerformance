export type QuotaState = 'available' | 'warning' | 'blocked'

export function quotaState(usedMb: number, warningMb: number, blockMb: number): QuotaState {
  if (usedMb < 0 || warningMb < 0 || blockMb <= warningMb) {
    throw new Error('Invalid free-tier quota values')
  }

  if (usedMb >= blockMb) return 'blocked'
  if (usedMb >= warningMb) return 'warning'
  return 'available'
}
