// Copyright © 2011, Kevin Cantu, me@kevincantu.org
//
// Permission to use, copy, modify, and/or distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
//
// The software is provided "as is" and the author disclaims all warranties
// with regard to this software including all implied warranties of
// merchantability and fitness.  In no event shall the author be liable for
// any special, direct, indirect, or consequential damanges or any damages
// whatsoever resulting from loss of use, data, your immortal soul, or 
// profits, whether in an action of contract, negligence, or other 
// tortious action, arising out of or in connection with the use or 
// performance of this software.

module Ninlil.Main

open Ninlil.Tumblr


// $
let inline (^<|) f a = f a


// init
let args = System.Environment.GetCommandLineArgs()
if args.Length <> 4
then
   printfn "Usage: Ninlil.exe BLOG EMAIL PASSWORD"
   exit 1

let [| _; blog; email; password |] = args
let api = Tumblr.API(blog, email, password)


// get the range from the oldest post to the most recent on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // date of post at index
   let dateOfPost (index: int) : System.DateTime = 
      let post = api.read index
      post.date


   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (target: System.DateTime) (start: int) : int =
      let nextPostDate = dateOfPost (start-1)

      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch target (start-1)
      

   // binsearch to find latest post on a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      let middle = (newest + oldest) / 2
      let middleDate = dateOfPost middle

      // match
      if target = middleDate then
         walkToNewestMatch target middle

      // too new
      elif target < middleDate then
         if (middle+1) <= oldest then
            findCutoff target (middle+1) oldest
         else
            oldest+1 // may be an overflow

      // too old
      else 
         if newest <= (middle-1) then
            findCutoff target newest (middle-1)
         else
            newest


   let total = api.totalPosts()

   // find where the end of the date we care about is
   let oldest = if total > 0 then total - 1 else 0  // assuming Tumblr numbers from 0
   let newest = findCutoff targetDate 0 oldest

   if newest > oldest then
      printfn "No range matches that requirement.  Exiting..."
      exit 0
   else
      printfn "Found posts from number %d to %d" oldest newest

   (oldest, newest)


// delete the range of posts on or before a given date
let deleteOnOrBefore (date: System.DateTime) =
      let (oldest, newest) = rangeEndingIn date

      let inc = 30 // arbitrary: 30 posts at a time

      // I could make this parallel and very fast, but 
      // let's be nice to Tumblr, we love them.
      //
      // Note: reads are positional, but 
      // the deletions and/or reblogging could be concurrent.

      let sleep secs = Async.RunSynchronously(Async.Sleep(secs * 1000)) |> ignore

      [newest..inc..oldest]
      |> List.map ^<| fun jj -> 
                        sleep 1
                        api.reads jj inc
      |> List.concat  // condense our array of post arrays
      |> List.map ^<| fun post ->
                        sleep 1
                        api.delete post.id
      |> fun l -> printfn "Done deleting %d posts" l.Length
   

// run
deleteOnOrBefore (System.DateTime(2011,6,1)) |> ignore


