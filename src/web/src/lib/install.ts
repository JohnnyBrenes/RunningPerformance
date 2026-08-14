export type InstallContext = {
  userAgent: string
  platform: string
  maxTouchPoints: number
  displayModeStandalone: boolean
  navigatorStandalone: boolean
}

export type InstallExperience = 'standalone' | 'ios-browser' | 'other-browser'

export function resolveInstallExperience(context: InstallContext): InstallExperience {
  if (context.displayModeStandalone || context.navigatorStandalone) return 'standalone'

  const classicIos = /iPhone|iPad|iPod/i.test(context.userAgent)
  const ipadDesktopMode = context.platform === 'MacIntel' && context.maxTouchPoints > 1

  return classicIos || ipadDesktopMode ? 'ios-browser' : 'other-browser'
}
