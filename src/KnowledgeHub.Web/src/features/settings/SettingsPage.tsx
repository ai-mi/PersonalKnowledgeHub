import { useMe } from '../auth/useMe'
import { ApiTokensSection } from './ApiTokensSection'
import { ProfileSection } from './ProfileSection'

export function SettingsPage() {
  const { data: me } = useMe()

  return (
    <>
      <h1>Настройки</h1>
      {me && <ProfileSection me={me} />}
      <ApiTokensSection />
    </>
  )
}
