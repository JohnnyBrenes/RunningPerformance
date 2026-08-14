import { describe, expect, it } from 'vitest'
import { resolveInstallExperience, type InstallContext } from './install'

const browserContext: InstallContext = {
  userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
  platform: 'Win32',
  maxTouchPoints: 0,
  displayModeStandalone: false,
  navigatorStandalone: false,
}

describe('resolveInstallExperience', () => {
  it('recognizes an iPhone browser that can use Add to Home Screen', () => {
    expect(resolveInstallExperience({
      ...browserContext,
      userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',
      platform: 'iPhone',
      maxTouchPoints: 5,
    })).toBe('ios-browser')
  })

  it('recognizes iPad desktop user agents through touch capability', () => {
    expect(resolveInstallExperience({
      ...browserContext,
      userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15)',
      platform: 'MacIntel',
      maxTouchPoints: 5,
    })).toBe('ios-browser')
  })

  it('prioritizes standalone display mode on any platform', () => {
    expect(resolveInstallExperience({ ...browserContext, displayModeStandalone: true })).toBe('standalone')
    expect(resolveInstallExperience({ ...browserContext, navigatorStandalone: true })).toBe('standalone')
  })

  it('keeps ordinary desktop tabs as another browser', () => {
    expect(resolveInstallExperience(browserContext)).toBe('other-browser')
  })
})
