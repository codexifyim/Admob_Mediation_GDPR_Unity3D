using System;
using System.Collections.Generic;
using UnityEngine;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using GoogleMobileAds.Common;

public class MonetizationManager : MonoBehaviour
{
    public static MonetizationManager Instance;

    [Header("Ad Unit IDs")]
    [SerializeField] private string rewardedId = "unused";
    [SerializeField] private string interstitialId = "unused";
    [SerializeField] private string bannerId = "unused";

    private RewardedAd rewardedAd;
    private InterstitialAd interstitialAd;
    private BannerView banner;

    private bool isInitialized;
    private bool canRequestAds;
    private bool isShowingAd;

    private float lastInterstitialTime;

    private int rewardedRetry;
    private int interstitialRetry;

    public bool IsRewardedReady => rewardedAd != null && rewardedAd.CanShowAd();
    public bool IsInterstitialReady => interstitialAd != null && interstitialAd.CanShowAd();

    public event Action OnRewardEarned;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        InitializeConsent();
    }

    #region INIT + CONSENT

    void InitializeConsent()
    {
        var request = new ConsentRequestParameters();

        ConsentInformation.Update(request, error =>
        {
            if (error != null)
            {
                Debug.LogWarning("Consent error, fallback to non-personalized");
                canRequestAds = false;
                InitializeAds();
                return;
            }

            if (ConsentInformation.ConsentStatus == ConsentStatus.Required)
            {
                ConsentForm.Load((form, loadError) =>
                {
                    if (loadError != null)
                    {
                        Debug.LogWarning("Consent load fail");
                        FinalizeConsent();
                        return;
                    }

                    form.Show(showError =>
                    {
                        if (showError != null)
                            Debug.LogWarning("Consent show fail");

                        FinalizeConsent();
                    });
                });
            }
            else
            {
                FinalizeConsent();
            }
        });
    }

    void FinalizeConsent()
    {
        canRequestAds =
            ConsentInformation.ConsentStatus == ConsentStatus.Obtained ||
            ConsentInformation.ConsentStatus == ConsentStatus.NotRequired;

        GoogleMobileAds.Mediation.UnityAds.Api.UnityAds.SetConsentMetaData("gdpr.consent", canRequestAds);
        GoogleMobileAds.Mediation.UnityAds.Api.UnityAds.SetConsentMetaData("privacy.consent", canRequestAds);

        InitializeAds();
    }

    void InitializeAds()
    {
        if (isInitialized) return;

        // Optional: test device
        var config = new RequestConfiguration
        {
            TestDeviceIds = new List<string> { AdRequest.TestDeviceSimulator }
        };
        MobileAds.SetRequestConfiguration(config);

        MobileAds.Initialize(_ =>
        {
            MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                isInitialized = true;

                if (canRequestAds)
                {
                    LoadRewarded();
                    LoadInterstitial();
                    LoadBanner();
                }
            });
        });
    }

    #endregion

    #region REWARDED

    public void LoadRewarded()
    {
        if (string.IsNullOrEmpty(rewardedId) || rewardedId == "unused") return;

        rewardedAd?.Destroy();
        rewardedAd = null;

        RewardedAd.Load(rewardedId, new AdRequest(), (ad, error) =>
        {
            if (error != null)
            {
                Retry(ref rewardedRetry, LoadRewarded);
                return;
            }

            rewardedAd = ad;
            rewardedRetry = 0;

            ad.OnAdFullScreenContentClosed += () =>
            {
                isShowingAd = false;
                LoadRewarded();
            };

            ad.OnAdFullScreenContentFailed += _ =>
            {
                isShowingAd = false;
                LoadRewarded();
            };

            ad.OnAdPaid += HandleAdRevenue;
        });
    }

    public void ShowRewarded(Action rewardAction = null)
    {
        if (isShowingAd || !IsRewardedReady) return;

        isShowingAd = true;

        rewardedAd.Show(_ =>
        {
            rewardAction?.Invoke();
            OnRewardEarned?.Invoke();
        });
    }

    #endregion

    #region INTERSTITIAL

    public void LoadInterstitial()
    {
        if (string.IsNullOrEmpty(interstitialId) || interstitialId == "unused") return;

        interstitialAd?.Destroy();
        interstitialAd = null;

        InterstitialAd.Load(interstitialId, new AdRequest(), (ad, error) =>
        {
            if (error != null)
            {
                Retry(ref interstitialRetry, LoadInterstitial);
                return;
            }

            interstitialAd = ad;
            interstitialRetry = 0;

            ad.OnAdFullScreenContentClosed += () =>
            {
                isShowingAd = false;
                LoadInterstitial();
            };

            ad.OnAdFullScreenContentFailed += _ =>
            {
                isShowingAd = false;
                LoadInterstitial();
            };

            ad.OnAdPaid += HandleAdRevenue;
        });
    }

    public void ShowInterstitial(float cooldown = 30f)
    {
        if (isShowingAd || !IsInterstitialReady) return;
        if (Time.time - lastInterstitialTime < cooldown) return;

        lastInterstitialTime = Time.time;
        isShowingAd = true;

        interstitialAd.Show();
    }

    #endregion

    #region BANNER

    public void LoadBanner()
    {
        if (string.IsNullOrEmpty(bannerId) || bannerId == "unused") return;

        banner?.Destroy();

        var size = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
        banner = new BannerView(bannerId, size, AdPosition.Bottom);

        banner.LoadAd(new AdRequest());
        banner.OnAdPaid += HandleAdRevenue;
    }

    public void ShowBanner() => banner?.Show();
    public void HideBanner() => banner?.Hide();

    #endregion

    #region SMART MONETIZATION

    public void ShowInterstitialOrRewarded(Action reward = null)
    {
        if (IsRewardedReady)
            ShowRewarded(reward);
        else
            ShowInterstitial();
    }

    #endregion

    #region UTIL

    void Retry(ref int counter, Action retryAction)
    {
        counter++;
        float delay = Mathf.Min(2f + counter * 2f, 30f);
        Invoke(nameof(ExecuteRetry), delay);

        void ExecuteRetry() => retryAction();
    }

    void HandleAdRevenue(AdValue value)
    {
        Debug.Log($"Ad Revenue: {value.Value} {value.CurrencyCode}");
        // Hook into Firebase / analytics here
    }

    void OnDestroy()
    {
        rewardedAd?.Destroy();
        interstitialAd?.Destroy();
        banner?.Destroy();
    }

    #endregion
}
