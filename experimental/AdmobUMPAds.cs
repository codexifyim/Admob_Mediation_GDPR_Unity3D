using System;
using System.Threading.Tasks;
using UnityEngine;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using GoogleMobileAds.Common;

public class AdmobUMPAds : MonoBehaviour
{
    private static AdmobUMPAds instance;
    public static AdmobUMPAds Instance => instance;

    [Header("AdMob Ad Unit IDs")]
    [SerializeField] private string _rewardedAdUnitId = "unused";
    [SerializeField] private string _interstitialAdUnitId = "unused";
    [SerializeField] private string _bannerAdUnitId = "unused";

    private RewardedAd _rewardedAd;
    private InterstitialAd _interstitialAd;
    private BannerView _bannerView;

    // State tracking
    private bool _isInitialized;
    private bool _isShowingAd;
    private int _rewardedRetryCount;
    private int _interstitialRetryCount;

    // Events
    public event Action OnRewardedAdCompleted;
    public event Action OnRewardedAdFailed;
    public event Action OnInterstitialAdClosed;
    public event Action<bool> OnConsentUpdated;

    private readonly TaskCompletionSource<bool> _initializationTcs = new();
    public Task InitializationComplete => _initializationTcs.Task;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        InitializeAdsWithConsent();
    }

    #region Consent & Initialization

    private void InitializeAdsWithConsent()
    {
        var request = new ConsentRequestParameters
        {
            TagForUnderAgeOfConsent = false
        };

        ConsentInformation.Update(request, updateError =>
        {
            if (updateError != null)
            {
                Debug.LogWarning($"UMP Update Error: {updateError.Message}. Proceeding to Init.");
                InitializeMobileAds();
                return;
            }

            if (ConsentInformation.ConsentStatus == ConsentStatus.Required)
            {
                ConsentForm.Load((form, loadError) =>
                {
                    if (loadError != null)
                    {
                        Debug.LogWarning($"UMP Load Error: {loadError.Message}. Proceeding to Init.");
                        FinalizeConsentAndInit();
                        return;
                    }

                    form.Show(showError =>
                    {
                        if (showError != null)
                            Debug.LogWarning($"UMP Show Error: {showError.Message}");

                        FinalizeConsentAndInit();
                    });
                });
            }
            else
            {
                FinalizeConsentAndInit();
            }
        });
    }

    private void FinalizeConsentAndInit()
    {
        bool canRequestAds =
            ConsentInformation.ConsentStatus == ConsentStatus.Obtained ||
            ConsentInformation.ConsentStatus == ConsentStatus.NotRequired;

        GoogleMobileAds.Mediation.UnityAds.Api.UnityAds.SetConsentMetaData("gdpr.consent", canRequestAds);
        GoogleMobileAds.Mediation.UnityAds.Api.UnityAds.SetConsentMetaData("privacy.consent", canRequestAds);

        OnConsentUpdated?.Invoke(canRequestAds);
        InitializeMobileAds();
    }

    private void InitializeMobileAds()
    {
        if (_isInitialized) return;

        MobileAds.Initialize(initStatus =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                _isInitialized = true;
                _initializationTcs.TrySetResult(true);

                LoadRewardedAd();
                LoadInterstitialAd();
                LoadBannerAd();
            });
        });
    }

    #endregion

    #region Rewarded Ads

    public void LoadRewardedAd()
    {
        if (string.IsNullOrEmpty(_rewardedAdUnitId) || _rewardedAdUnitId == "unused") return;

        _rewardedAd?.Destroy();
        _rewardedAd = null;

        RewardedAd.Load(_rewardedAdUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null)
            {
                _rewardedRetryCount++;
                Invoke(nameof(LoadRewardedAd), GetRetryDelay(_rewardedRetryCount));
                return;
            }

            _rewardedAd = ad;
            _rewardedRetryCount = 0;
            RegisterRewardedAdEvents(_rewardedAd);
        });
    }

    private void RegisterRewardedAdEvents(RewardedAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                _isShowingAd = false;
                LoadRewardedAd();
            });
        };

        ad.OnAdFullScreenContentFailed += (error) =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                _isShowingAd = false;
                OnRewardedAdFailed?.Invoke();
                LoadRewardedAd();
            });
        };
    }

    public void ShowRewardedAd(Action onAdCompleted = null)
    {
        if (_isShowingAd) return;

        if (_rewardedAd != null && _rewardedAd.CanShowAd())
        {
            _isShowingAd = true;

            _rewardedAd.Show(reward =>
            {
                MobileAdsEventExecutor.ExecuteInUpdate(() =>
                {
                    onAdCompleted?.Invoke();
                    OnRewardedAdCompleted?.Invoke();
                });
            });
        }
        else
        {
            LoadRewardedAd();
        }
    }

    #endregion

    #region Interstitial Ads

    public void LoadInterstitialAd()
    {
        if (string.IsNullOrEmpty(_interstitialAdUnitId) || _interstitialAdUnitId == "unused") return;

        _interstitialAd?.Destroy();
        _interstitialAd = null;

        InterstitialAd.Load(_interstitialAdUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null)
            {
                _interstitialRetryCount++;
                Invoke(nameof(LoadInterstitialAd), GetRetryDelay(_interstitialRetryCount));
                return;
            }

            _interstitialAd = ad;
            _interstitialRetryCount = 0;
            RegisterInterstitialEvents(_interstitialAd);
        });
    }

    private void RegisterInterstitialEvents(InterstitialAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                _isShowingAd = false;
                OnInterstitialAdClosed?.Invoke();
                LoadInterstitialAd();
            });
        };

        ad.OnAdFullScreenContentFailed += (err) =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                _isShowingAd = false;
                LoadInterstitialAd();
            });
        };
    }

    public void ShowInterstitialAd()
    {
        if (_isShowingAd) return;

        if (_interstitialAd != null && _interstitialAd.CanShowAd())
        {
            _isShowingAd = true;
            _interstitialAd.Show();
        }
        else
        {
            LoadInterstitialAd();
        }
    }

    #endregion

    #region Banner Ads

    public void LoadBannerAd()
    {
        if (string.IsNullOrEmpty(_bannerAdUnitId) || _bannerAdUnitId == "unused") return;

        DestroyBanner();

        AdSize adaptiveSize = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
        _bannerView = new BannerView(_bannerAdUnitId, adaptiveSize, AdPosition.Bottom);

        _bannerView.LoadAd(new AdRequest());
    }

    public void HideBanner() => _bannerView?.Hide();
    public void ShowBanner() => _bannerView?.Show();

    public void DestroyBanner()
    {
        if (_bannerView != null)
        {
            _bannerView.Destroy();
            _bannerView = null;
        }
    }

    #endregion

    #region Helpers

    private float GetRetryDelay(int count)
    {
        return Mathf.Min(Mathf.Pow(2, count), 64f);
    }

    private void OnDestroy()
    {
        _rewardedAd?.Destroy();
        _interstitialAd?.Destroy();
        DestroyBanner();
    }

    #endregion
}