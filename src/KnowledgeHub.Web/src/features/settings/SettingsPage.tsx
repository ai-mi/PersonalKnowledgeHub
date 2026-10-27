import { useMe } from '../auth/useMe'
import { ApiTokensSection } from './ApiTokensSection'
import { DigestSection } from './DigestSection'
import { ProfileSection } from './ProfileSection'
import { TelegramSection } from './TelegramSection'

export function SettingsPage() {
  const { data: me } = useMe()

  return (
    <>
      <h1>Настройки</h1>
      {me && <ProfileSection me={me} />}
      {me && <DigestSection me={me} />}
      <TelegramSection />
      <ApiTokensSection />
    </>
  )
}
