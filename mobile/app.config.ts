import type { ConfigContext, ExpoConfig } from 'expo/config';

export default ({ config }: ConfigContext): ExpoConfig => {
  const plugins = [...(config.plugins ?? [])];
  const iosUrlScheme = process.env.EXPO_PUBLIC_GOOGLE_IOS_URL_SCHEME;

  if (iosUrlScheme) {
    plugins.push([
      'react-native-nitro-google-signin',
      { iosUrlScheme },
    ]);
  }

  return {
    ...config,
    name: 'Xumora',
    slug: 'xumora',
    scheme: 'xumora',
    ios: {
      ...config.ios,
      bundleIdentifier: 'com.xumora.mobile',
    },
    android: {
      ...config.android,
      package: 'com.xumora.mobile',
    },
    plugins,
  };
};
