﻿using System;
using System.Collections.Generic;
using System.Threading;
using TNRD.Zeepkist.GTR.Messaging;
using UnityEngine;
using ZeepkistClient;
using ZeepSDK.External.Cysharp.Threading.Tasks;
using ZeepSDK.External.FluentResults;
using ZeepSDK.Leaderboard.Pages;
using ZeepSDK.Level;

namespace TNRD.Zeepkist.GTR.Leaderboard;

public class OnlineLeaderboardTab : BaseMultiplayerLeaderboardTab
{
    private readonly LeaderboardGraphqlService _graphqlService;
    private readonly MessengerService _messengerService;
    private readonly List<IGetPersonalBests_Records_Nodes> _items = [];

    private CancellationTokenSource _cancellationTokenSource;

    private static readonly Dictionary<int, double> Fibbonus = new()
    {
        { 0, 0.21 },
        { 1, 0.13 },
        { 2, 0.08 },
        { 3, 0.05 },
        { 4, 0.03 },
        { 5, 0.02 },
        { 6, 0.01 },
        { 7, 0.01 }
    };

    private int _totalUsers;
    private int _personalBests;

    public OnlineLeaderboardTab(LeaderboardGraphqlService graphqlService, MessengerService messengerService)
    {
        _graphqlService = graphqlService;
        _messengerService = messengerService;
    }

    protected override string GetLeaderboardTitle()
    {
        return "GTR Records";
    }

    protected override void OnEnable()
    {
        InitializeAsync().Forget();
    }

    protected override void OnDisable()
    {
    }

    protected override void OnDraw()
    {
        for (int i = 0; i < Instance.leaderboard_tab_positions.Count; i++)
        {
            GUI_OnlineLeaderboardPosition gui = Instance.leaderboard_tab_positions[i];
            if (i >= _items.Count)
            {
                continue;
            }

            gui.gameObject.SetActive(true);
            IGetPersonalBests_Records_Nodes item = _items[i];
            int index = CurrentPage * Instance.leaderboard_tab_positions.Count + i;
            OnDrawItem(gui, item, index);
        }
    }

    protected override void OnPageChanged(int previous, int current)
    {
        LoadRecords(current);
    }

    private async UniTaskVoid InitializeAsync()
    {
        Result<int> personalBestCount = await _graphqlService.GetPersonalBestCount(LevelApi.CurrentHash);
        if (personalBestCount.IsFailed)
        {
            Logger.LogError("Failed to get count");
            // TODO: Handle
            return;
        }

        _personalBests = personalBestCount.Value;
        MaxPages = _personalBests / Instance.leaderboard_tab_positions.Count;
        UpdatePageNumber();

        LoadRecords(CurrentPage);
    }

    private void LoadRecords(int page)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        LoadRecords(page, _cancellationTokenSource.Token).Forget();
    }

    private async UniTaskVoid LoadRecords(int page, CancellationToken ct)
    {
        _items.Clear();
        Draw();

        UniTask<Result<IGetPersonalBestsResult>> recordsTask =
            _graphqlService.GetLeaderboardRecords(LevelApi.CurrentHash, page, ct);
        UniTask<Result<int>> userCountTask = _graphqlService.GetTotalUserCount(ct);

        (Result<IGetPersonalBestsResult> recordsResult, Result<int> userCountResult) =
            await UniTask.WhenAll(recordsTask, userCountTask);

        if (ct.IsCancellationRequested)
            return;

        if (recordsResult.IsFailed)
        {
            Logger.LogError("Failed to load GTR records: " + recordsResult);
            _messengerService.LogError("Failed to load GTR records");
            return;
        }

        if (recordsResult.IsFailed)
        {
            Logger.LogError("Failed to get user count: " + recordsResult);
            _messengerService.LogError("Failed to load GTR records");
            return;
        }

        _totalUsers = userCountResult.Value;
        _items.Clear();
        _items.AddRange(recordsResult.Value.Records!.Nodes);
        Draw();
    }

    private void OnDrawItem(GUI_OnlineLeaderboardPosition gui, IGetPersonalBests_Records_Nodes item, int index)
    {
        ZeepkistNetwork.TryGetPlayer(Convert.ToUInt64(item.User.SteamId), out gui.thePlayer);

        gui.position.gameObject.SetActive(true);
        gui.position.text = (index + 1).ToString();
        gui.position.color = PlayerManager.Instance.GetColorFromPosition(index + 1);
        gui.favoriteButton.gameObject.SetActive(false);
        ColorUtility.ToHtmlStringRGB(PlayerManager.Instance.GetChatColor());

        if (ZeepkistNetwork.LocalPlayer.SteamID.ToString() == item.User.SteamId)
        {
            string playerColor = ColorUtility.ToHtmlStringRGB(ZeepkistNetwork.LocalPlayer.chatColor);
            gui.player_name.text =
                $"<color=#{playerColor}><link=\"{item.User.SteamId}\">{item.User.SteamName}</link></color>";
        }
        else if (gui.thePlayer != null && gui.thePlayer.SteamID.ToString() == item.User.SteamId)
        {
            string playerColor = ColorUtility.ToHtmlStringRGB(gui.thePlayer.chatColor);
            gui.player_name.text =
                $"<color=#{playerColor}><link=\"{item.User.SteamId}\">{item.User.SteamName}</link></color>";
        }
        else
            gui.player_name.text = $"<link=\"{item.User.SteamId}\">{item.User.SteamName}</link>";

        gui.time.text = item.Time.GetFormattedTime();

        int placementPoints = Math.Max(0, _personalBests - index);
        double a = 1d / (_totalUsers / (double)_personalBests);
        int b = index + 1;
        double c = index < 8 ? Fibbonus[index] : 0;
        double points = placementPoints * (1 + a / b) + c;

        gui.pointsWon.gameObject.SetActive(true);
        gui.pointsWon.text = $"(+{(int)Math.Round(points)})";
    }
}
