module SubscriptionsPage

open System
open Lit
open Lit.Elmish
open Shared
open ValidatedInput
open Validus
open Validus.Operators

// TODO: Delete feed.

type State = {
  FeedName: ValidationState<string>
  FeedUrl: ValidationState<string>
  SubscribedFeeds: SubscribedFeed list
  Error: string
}

type Msg =
  | SetFeedName of string
  | SetFeedUrl of string
  | SetSubscribedFeeds of SubscribedFeed list
  | Subscribe
  | SubscriptionResult of Result<SubscribedFeed, SubscribeToFeedError>

let init () =
  {
    FeedName = ValidationState.createInvalidWithNoErrors "Feed name" String.Empty
    FeedUrl = ValidationState.createInvalidWithNoErrors "Feed url" String.Empty
    SubscribedFeeds = []
    Error = ""
  },
  Elmish.Cmd.OfAsync.perform Remoting.securedServerApi.GetSubscribedFeeds () SetSubscribedFeeds

let update (msg: Msg) (state: State) =
  match msg with
  | SetFeedName feedName ->
    let feedNameValidator = Check.String.notEmpty
    let feedNameState = ValidationState.create (feedNameValidator "Feed name") feedName
    { state with FeedName = feedNameState }, Elmish.Cmd.none
  | SetFeedUrl feedUrl ->
    let feedUrlValidator = Check.String.notEmpty // TODO: Check for valid url???????
    let feedUrlState = ValidationState.create (feedUrlValidator "Feed url") feedUrl
    { state with FeedUrl = feedUrlState }, Elmish.Cmd.none
  | SetSubscribedFeeds subscribedFeeds -> { state with SubscribedFeeds = subscribedFeeds }, Elmish.Cmd.none
  | Subscribe ->
    let cmd =
      match state.FeedName, state.FeedUrl with
      | Valid feedName, Valid feedUrl ->
        Elmish.Cmd.OfAsync.perform
          Remoting.securedServerApi.SubscribeToFeed
          {
            FeedName = feedName
            FeedUrl = feedUrl
          }
          SubscriptionResult
      | _ -> Elmish.Cmd.none

    state, cmd
  | SubscriptionResult result ->
    match result with
    | Ok subscribedFeed -> { state with SubscribedFeeds = subscribedFeed :: state.SubscribedFeeds }, Elmish.Cmd.none
    | Error error ->
      match error with
      | AlreadySubscribed -> { state with Error = "You are already subscribed to that feed" }, Elmish.Cmd.none

let tableRow (subscribedFeed: SubscribedFeed) =
  html
    $"""
    <tr class="bg-white border-b dark:bg-gray-800 dark:border-gray-700">
      <th scope="row" class="py-4 px-6 font-medium text-gray-900 whitespace-nowrap dark:text-white">
          {subscribedFeed.Name}
      </th>
      <td class="py-4 px-6">
          {subscribedFeed.FeedUrl}
      </td>
      <td class="py-4 px-6">
          <button  class="text-white bg-blue-700 hover:bg-blue-800 focus:ring-4 focus:outline-none focus:ring-blue-300 font-medium rounded-lg text-sm px-5 py-2.5 text-center dark:bg-blue-600 dark:hover:bg-blue-700 dark:focus:ring-blue-800">Delete</button>
      </td>
    </tr>
    """

[<HookComponent>]
let Component () =
  let state, dispatch = Hook.useElmish (init, update)

  let renderError error =
    html $"""<p class="text-red-500">{error}</p>"""

  html
    $"""
    <div class="w-full flex flex-col gap-y-3 justify-center items-center pt-20">
      <div class="flex flex-col sm:flex-row justify-center items-center w-full gap-x-3">
        <div class="mb-6" colspan="3">
          <label for="feed-name" class="block mb-2 text-sm font-medium text-gray-900 dark:text-white">Feed name</label>
          <input @change={EvVal(SetFeedName >> dispatch)} placeholder="feed name" type="text" id="feed-name" class="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-lg focus:ring-blue-500 focus:border-blue-500 block w-full p-2.5 dark:bg-gray-700 dark:border-gray-600 dark:placeholder-gray-400 dark:text-white dark:focus:ring-blue-500 dark:focus:border-blue-500">
        </div>
        <div class="mb-6" colspan="3">
          <label for="feed-url" class="block mb-2 text-sm font-medium text-gray-900 dark:text-white">RSS feed url</label>
          <input @change={EvVal(SetFeedUrl >> dispatch)} placeholder="feed url" type="text" id="feed-url" class="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-lg focus:ring-blue-500 focus:border-blue-500 block w-full p-2.5 dark:bg-gray-700 dark:border-gray-600 dark:placeholder-gray-400 dark:text-white dark:focus:ring-blue-500 dark:focus:border-blue-500">
        </div>
        <button @click={Ev(fun _ -> dispatch Subscribe)} class="text-white bg-blue-700 hover:bg-blue-800 focus:ring-4 focus:outline-none focus:ring-blue-300 font-medium rounded-lg text-sm px-5 py-2.5 text-center dark:bg-blue-600 dark:hover:bg-blue-700 dark:focus:ring-blue-800">Subscribe</button>
      </div>
      <div class="overflow-x-auto relative">
        <table class="w-full text-sm text-left text-gray-500 dark:text-gray-400">
          <thead class="text-xs text-gray-700 uppercase bg-gray-50 dark:bg-gray-700 dark:text-gray-400">
            <tr>
              <th scope="col" class="py-3 px-6">
                  Feed
              </th>
              <th scope="col" class="py-3 px-6">
                  RSS Url
              </th>
              <th scope="col" class="py-3 px-6">
                  Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {state.SubscribedFeeds |> List.map tableRow}
          </tbody>
        </table>
      </div>
    </div>
    """
