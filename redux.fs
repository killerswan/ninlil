// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts


(*
open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing
*)

open Tumblr





// command line args /////////////////////////////////////////////

let args = System.Environment.GetCommandLineArgs()
if args.Length <> 4
then
   printfn "Usage: redux.exe BLOG EMAIL PASSWORD"
   // yes, this API needs some TLS, stat!
   exit 1

let [| _; (blog: string); (email: string); (password: string) |] = args

let api = Tumblr.API(blog, email, password)




// test
let testPostReblogging ii = 
   // read a post (or list of posts)
   let (start, total, posts) = api.readAndProcess (ii, 1)

   // reblog and delete that post (or list of posts)
   posts |> List.map (fun post ->
         api.reblog post.id post.rkey  |> ignore
         api.delete post.id            |> ignore
   )


// dealing with a range ////////////////////////////////

// get the range from the oldest post to the most recent on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // date of post /////////////////////////////////////////////
   let dateOfPost (index: int) : System.DateTime = 
      let (start, total, posts) = api.readAndProcess (index, 1)

      let post1 = List.head posts
      post1.date


   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (target: System.DateTime) (start: int) : int =
      let nextPostDate = dateOfPost (start-1)

      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch target (start-1)
      

   // binsearch to find latest post on a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      if (newest + 2) = oldest then

         // maybe no match, but stop recursing
         if target < (dateOfPost oldest) then
            oldest + 1
         elif target < (dateOfPost (newest + 1)) then
            oldest
         elif target < (dateOfPost newest) then
            newest + 1
         else
            newest

      else
         let middle = (newest + oldest) / 2

         // match
         if target = (dateOfPost middle) then
            walkToNewestMatch target middle

         // too new
         elif target < (dateOfPost middle) then
            findCutoff target        (middle+1) oldest

         // too old
         else 
            findCutoff target newest (middle-1)


   // get the latest post
   let (startingPostNumber, total, posts) = api.readAndProcess (0, 1)

   // find where the end of the date we care about is
   let oldest = total - 1  // assuming Tumblr numbers from 0
   let newest = findCutoff targetDate startingPostNumber oldest

   (oldest, newest)


// delete the range of posts on or before a given date
let deleteOnOrBefore (date: System.DateTime) =
      let (oldest, newest) = rangeEndingIn date
   
      // arbitrarily do requests for 30 posts at a time
      let inc = 30

      // I could make this parallel and very fast, but 
      // let's be nice to Tumblr, we love them.
      //
      // Note: reads are positional, but the id and reblog key
      // would allow us to easily do deletions or reblogging after that
      // concurrently.
      [newest..inc..oldest] 
      |> List.map (fun jj -> 
            Async.RunSynchronously(Async.Sleep(10*1000)) |> ignore
            let (_, _, posts) = api.readAndProcess (jj, inc)
            posts)
      |> List.concat  // condense our array of post arrays
      |> List.map (fun post ->
(*
            Async.RunSynchronously(Async.Sleep(5*1000)) |> ignore  // is there some obvious sleep command?
            api.reblog post.id post.rkey |> ignore
*)
            Async.RunSynchronously(Async.Sleep(3*1000)) |> ignore
            api.delete post.id |> ignore)
   

// run /////////////////////////////////////////////

//testPostReblogging 270 |> ignore

deleteOnOrBefore (System.DateTime(2010,1,3)) |> ignore


