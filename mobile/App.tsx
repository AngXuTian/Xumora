import { useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Platform,
  Pressable,
  SafeAreaView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { StatusBar } from 'expo-status-bar';
import * as SecureStore from 'expo-secure-store';
import {
  GoogleOneTapSignIn,
  isNoSavedCredentialFoundResponse,
  isSuccessResponse,
} from 'react-native-nitro-google-signin';
import {
  ApiError,
  getProfile,
  isApiConfigured,
  refreshSession,
  revokeSession,
  signInWithGoogle,
  type AuthenticationSession,
} from './src/authApi';

const SESSION_KEY = 'xumora.auth.session';
const GOOGLE_WEB_CLIENT_ID =
  process.env.EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID?.trim() ?? '';
const GOOGLE_IOS_CLIENT_ID =
  process.env.EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID?.trim() ?? '';
const GOOGLE_IOS_URL_SCHEME =
  process.env.EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME?.trim() ?? '';
const googleConfigured =
  Platform.OS === 'android'
    ? Boolean(GOOGLE_WEB_CLIENT_ID)
    : Platform.OS === 'ios'
      ? Boolean(
          GOOGLE_WEB_CLIENT_ID &&
            GOOGLE_IOS_CLIENT_ID &&
            GOOGLE_IOS_URL_SCHEME,
        )
      : false;
const canSignIn = isApiConfigured && googleConfigured;

function configureGoogleSignIn() {
  if (!googleConfigured) {
    throw new Error('Google sign-in is not configured for this device.');
  }

  GoogleOneTapSignIn.configure({
    webClientId: GOOGLE_WEB_CLIENT_ID,
    ...(GOOGLE_IOS_CLIENT_ID ? { iosClientId: GOOGLE_IOS_CLIENT_ID } : {}),
  });
}

function isAuthenticationSession(value: unknown): value is AuthenticationSession {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const session = value as Partial<AuthenticationSession>;
  return (
    typeof session.accessToken === 'string' &&
    typeof session.refreshToken === 'string' &&
    typeof session.user?.id === 'string' &&
    typeof session.user?.email === 'string'
  );
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong.';
}

export default function App() {
  const [session, setSession] = useState<AuthenticationSession | null>(null);
  const [isRestoring, setIsRestoring] = useState(true);
  const [isBusy, setIsBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    const restore = async () => {
      let savedSession: AuthenticationSession | null = null;
      try {
        const stored = await SecureStore.getItemAsync(SESSION_KEY);
        if (!stored || !isMounted) {
          return;
        }

        let saved: unknown;
        try {
          saved = JSON.parse(stored);
        } catch {
          await SecureStore.deleteItemAsync(SESSION_KEY);
          return;
        }

        if (!isAuthenticationSession(saved)) {
          await SecureStore.deleteItemAsync(SESSION_KEY);
          return;
        }
        savedSession = saved;

        try {
          const user = await getProfile(saved.accessToken);
          const current = { ...saved, user };
          await SecureStore.setItemAsync(SESSION_KEY, JSON.stringify(current));
          if (isMounted) {
            setSession(current);
          }
        } catch (profileError) {
          if (profileError instanceof ApiError && profileError.status === 404) {
            await SecureStore.deleteItemAsync(SESSION_KEY);
            return;
          }

          if (!(profileError instanceof ApiError) || profileError.status !== 401) {
            throw profileError;
          }

          try {
            const refreshed = await refreshSession(saved.refreshToken);
            await SecureStore.setItemAsync(
              SESSION_KEY,
              JSON.stringify(refreshed),
            );
            if (isMounted) {
              setSession(refreshed);
            }
          } catch (refreshError) {
            if (refreshError instanceof ApiError && refreshError.status === 401) {
              await SecureStore.deleteItemAsync(SESSION_KEY);
              return;
            }

            throw refreshError;
          }
        }
      } catch (restoreError) {
        if (isMounted) {
          setSession(savedSession);
          setError(errorMessage(restoreError));
        }
      } finally {
        if (isMounted) {
          setIsRestoring(false);
        }
      }
    };

    void restore();

    return () => {
      isMounted = false;
    };
  }, []);

  const handleGoogleSignIn = async () => {
    setError(null);
    setIsBusy(true);

    try {
      configureGoogleSignIn();
      await GoogleOneTapSignIn.checkPlayServices();

      let response = await GoogleOneTapSignIn.signIn();
      if (isNoSavedCredentialFoundResponse(response)) {
        response = await GoogleOneTapSignIn.createAccount();
      }
      if (isNoSavedCredentialFoundResponse(response)) {
        response = await GoogleOneTapSignIn.presentExplicitSignIn();
      }
      if (!isSuccessResponse(response)) {
        return;
      }

      if (!response.data.idToken) {
        throw new Error('Google did not return an ID token. Please try again.');
      }

      const authenticated = await signInWithGoogle(response.data.idToken);
      await SecureStore.setItemAsync(
        SESSION_KEY,
        JSON.stringify(authenticated),
      );
      setSession(authenticated);
    } catch (signInError) {
      setError(errorMessage(signInError));
    } finally {
      setIsBusy(false);
    }
  };

  const handleSignOut = async () => {
    if (!session) {
      return;
    }

    setError(null);
    setIsBusy(true);
    let signOutWarning: string | null = null;

    try {
      await revokeSession(session.refreshToken);
    } catch (revokeError) {
      signOutWarning = `Could not revoke the server session: ${errorMessage(revokeError)}`;
    }

    if (googleConfigured) {
      try {
        configureGoogleSignIn();
        await GoogleOneTapSignIn.signOut();
      } catch (googleSignOutError) {
        signOutWarning ??= errorMessage(googleSignOutError);
      }
    }

    let cleanupError: string | null = null;
    try {
      await SecureStore.deleteItemAsync(SESSION_KEY);
      setSession(null);
    } catch (storageError) {
      cleanupError = `Could not clear the saved session: ${errorMessage(storageError)}`;
      setError(cleanupError);
    } finally {
      setIsBusy(false);
    }

    if (!cleanupError && signOutWarning) {
      setError(signOutWarning);
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar style="dark" />
      <View style={styles.container}>
        <View style={styles.brandMark}>
          <Text style={styles.brandMarkText}>X</Text>
        </View>
        <Text style={styles.wordmark}>Xumora</Text>

        {isRestoring ? (
          <View style={styles.statePanel}>
            <ActivityIndicator color="#1e5b46" size="large" />
            <Text style={styles.stateText}>Restoring your session…</Text>
          </View>
        ) : session ? (
          <View style={styles.content}>
            <Text style={styles.eyebrow}>SIGNED IN</Text>
            <Text style={styles.title}>Welcome back</Text>
            <Text style={styles.description}>{session.user.email}</Text>
            <Pressable
              accessibilityRole="button"
              disabled={isBusy}
              onPress={handleSignOut}
              style={({ pressed }) => [
                styles.secondaryButton,
                pressed && styles.pressed,
                isBusy && styles.disabled,
              ]}
            >
              {isBusy ? (
                <ActivityIndicator color="#1e5b46" />
              ) : (
                <Text style={styles.secondaryButtonText}>Sign out</Text>
              )}
            </Pressable>
          </View>
        ) : (
          <View style={styles.content}>
            <Text style={styles.eyebrow}>PHOTOGRAPHY, WITH A GUIDE</Text>
            <Text style={styles.title}>Make every shot count.</Text>
            <Text style={styles.description}>
              Sign in to get started with your personal AI photography assistant.
            </Text>

            <Pressable
              accessibilityRole="button"
              disabled={!canSignIn || isBusy}
              onPress={handleGoogleSignIn}
              style={({ pressed }) => [
                styles.googleButton,
                pressed && styles.pressed,
                (!canSignIn || isBusy) && styles.disabled,
              ]}
            >
              {isBusy ? (
                <ActivityIndicator color="#fff" />
              ) : (
                <>
                  <Text style={styles.googleGlyph}>G</Text>
                  <Text style={styles.googleButtonText}>
                    Continue with Google
                  </Text>
                </>
              )}
            </Pressable>

            <Text style={styles.privacyNote}>
              Your Google ID token is exchanged securely with Xumora and is not
              saved on this device.
            </Text>
          </View>
        )}

        {error ? (
          <Text accessibilityRole="alert" style={styles.error}>
            {error}
          </Text>
        ) : null}
        {!isRestoring && !session && !canSignIn ? (
          <Text style={styles.setupNote}>
            Configure the API URL and Google OAuth client IDs in mobile/.env.
            Google sign-in requires an iOS or Android development build.
          </Text>
        ) : null}
        <Text style={styles.footer}>A little more intention. A lot more magic.</Text>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: '#f5f4ef',
  },
  container: {
    flex: 1,
    width: '100%',
    maxWidth: 520,
    alignSelf: 'center',
    justifyContent: 'center',
    paddingHorizontal: 28,
    paddingVertical: 36,
  },
  brandMark: {
    width: 48,
    height: 48,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: 16,
    backgroundColor: '#1e5b46',
  },
  brandMarkText: {
    color: '#fff',
    fontSize: 24,
    fontWeight: '700',
  },
  wordmark: {
    marginTop: 12,
    color: '#1b2923',
    fontSize: 18,
    fontWeight: '700',
    letterSpacing: 0.2,
  },
  content: {
    marginTop: 72,
  },
  eyebrow: {
    color: '#567668',
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 1.8,
  },
  title: {
    marginTop: 14,
    color: '#1b2923',
    fontSize: 36,
    fontWeight: '700',
    letterSpacing: -1.1,
    lineHeight: 42,
  },
  description: {
    marginTop: 14,
    color: '#65736c',
    fontSize: 16,
    lineHeight: 24,
  },
  googleButton: {
    minHeight: 56,
    marginTop: 32,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 12,
    borderRadius: 14,
    backgroundColor: '#1e5b46',
  },
  googleGlyph: {
    color: '#fff',
    fontSize: 18,
    fontWeight: '700',
  },
  googleButtonText: {
    color: '#fff',
    fontSize: 15,
    fontWeight: '600',
  },
  secondaryButton: {
    minHeight: 54,
    marginTop: 32,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: '#b8c5bd',
    borderRadius: 14,
    backgroundColor: '#fff',
  },
  secondaryButtonText: {
    color: '#1e5b46',
    fontSize: 15,
    fontWeight: '600',
  },
  privacyNote: {
    marginTop: 18,
    color: '#7b8781',
    fontSize: 12,
    lineHeight: 18,
    textAlign: 'center',
  },
  statePanel: {
    marginTop: 96,
    alignItems: 'center',
    gap: 16,
  },
  stateText: {
    color: '#65736c',
    fontSize: 14,
  },
  setupNote: {
    marginTop: 22,
    color: '#8a6c38',
    fontSize: 12,
    lineHeight: 18,
    textAlign: 'center',
  },
  error: {
    marginTop: 22,
    color: '#a23b38',
    fontSize: 13,
    lineHeight: 19,
    textAlign: 'center',
  },
  footer: {
    marginTop: 'auto',
    paddingTop: 40,
    color: '#9aa39e',
    fontSize: 12,
    textAlign: 'center',
  },
  pressed: {
    opacity: 0.86,
  },
  disabled: {
    opacity: 0.55,
  },
});
